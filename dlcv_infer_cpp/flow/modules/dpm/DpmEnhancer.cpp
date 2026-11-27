#include "DpmEnhancer.h"
#include "DpmNumeric.h"
#include "DpmVision.h"
#include "DpmEcc.h"
#include <chrono>
#include <stdexcept>

namespace dlcv_infer { namespace flow { namespace dpm {
namespace {
using Clock=std::chrono::steady_clock;
double elapsed(Clock::time_point a, Clock::time_point b) { return std::chrono::duration<double,std::milli>(b-a).count(); }
const std::vector<float> SrcCorners={0,0,31,0,31,31,0,31};
const std::vector<float> SrcAsym={-.65f,.05f,31.05f,.05f,31.05f,31.65f,-.65f,31.65f};
const std::vector<float> SrcOb1={-.08f,-.04f,31.04f,.02f,31.f,31.04f,-.10f,31.05f};
const std::vector<float> SrcOb2={-.03f,0,30.98f,.01f,31.10f,31.09f,-.11f,31.15f};
const std::vector<float> DstRect={21,21,207,21,207,207,21,207};
const std::vector<float> GridPts32=[] { std::vector<float> p; for(int r=0;r<32;++r) for(int c=0;c<32;++c) {p.push_back(float(c));p.push_back(float(r));} return p; }();
}
EnhancementResult Enhance(const cv::Mat& gray, const std::function<std::string(const cv::Mat&)>& decode) {
    if(gray.empty() || gray.type()!=CV_8UC1) throw std::invalid_argument("DPM requires a non-empty uint8 gray image");
    const int w=gray.cols,h=gray.rows,upscaleModulePx=8,quietZonePx=32;
    std::vector<uint8_t> grayRoi(w*h);
    for(int y=0;y<h;++y) std::copy_n(gray.ptr<uint8_t>(y),w,grayRoi.begin()+y*w);
    auto t0=Clock::now();
        // 三边静区 60 分位估计基底金属亮度，抗局部阴影与角落暗区
        int qzW = std::max(1, (int)(w * 0.90));
        int qzH = std::min(8, h);
        int lftW = std::min(8, w);
        std::vector<uint8_t> qzSample(2 * qzW * qzH + lftW * h);
        int qzPos = 0;
        for (int y = 0; y < qzH; y++)
        {
            std::copy_n(grayRoi.begin() + y * w, qzW, qzSample.begin() + qzPos);
            qzPos += qzW;
            std::copy_n(grayRoi.begin() + (h - qzH + y) * w, qzW, qzSample.begin() + qzPos);
            qzPos += qzW;
        }
        for (int y = 0; y < h; y++)
        {
            std::copy_n(grayRoi.begin() + y * w, lftW, qzSample.begin() + qzPos);
            qzPos += lftW;
        }
        float qzMed = vision::ComputePercentileU8(qzSample, 0.60);
        uint8_t qzFill = (uint8_t)std::clamp((int)std::nearbyint(qzMed), 0, 255);

        std::vector<uint8_t> imC = grayRoi;
        if (w >= 195)
        {
            int xCut = (int)(w * 0.95);
            for (int y = 0; y < h; y++)
            {
                int rOff = y * w;
                for (int x = xCut; x < w; x++) imC[rOff + x] = qzFill;
            }
        }

        // S1: 1/4 尺度快速形态学上包络参考 + 双级高光修复 + 5x5 正脉冲星点脱敏
        float p88 = vision::ComputePercentileU8(imC, 0.88);
        float capPre = std::max(qzMed + 22.0f, p88);
        std::vector<float> imCapPre(w * h);
        for (int i = 0; i < int(imC.size()); i++)
            imCapPre[i] = imC[i] < capPre ? imC[i] : capPre;

        int wQ4 = std::max(16, w / 4), hQ4 = std::max(16, h / 4);
        std::vector<float> imQ4 = vision::ResizeAreaF32(imCapPre, w, h, wQ4, hQ4);
        std::vector<float> closeQ4 = vision::MorphCloseEllipseF32(imQ4, wQ4, hQ4, 17);
        std::vector<float> blurQ4 = vision::GaussianBlurF32(closeQ4, wQ4, hQ4, 14.0);
        std::vector<float> bgPre = vision::ResizeBilinearF32(blurQ4, wQ4, hQ4, w, h);

        std::vector<uint8_t> candGlare(w * h);
        int xGlintMax = (int)(w * 0.94);
        for (int y = 6; y < h - 6; y++)
        {
            int rOff = y * w;
            for (int x = 6; x < xGlintMax; x++)
            {
                int idx = rOff + x;
                if (imC[idx] > bgPre[idx] + 18.0f || imC[idx] > 235)
                    candGlare[idx] = 255;
            }
        }

        auto [glareLbl, glareComps] = vision::ConnectedComponents8(candGlare, w, h, false);
        bool anySmall = false, anyLarge = false;
        std::vector<uint8_t> smallGlint(w * h);
        std::vector<uint8_t> largeGlare(w * h);
        if (int(glareComps.size()) > 0)
        {
            std::vector<uint8_t> compType(int(glareComps.size()) + 1);
            for (const auto& comp : glareComps)
            {
                if (comp.Area <= 45) { compType[comp.Label] = 1; anySmall = true; }
                else { compType[comp.Label] = 2; anyLarge = true; }
            }
            for (int i = 0; i < int(glareLbl.size()); i++)
            {
                uint8_t t = compType[glareLbl[i]];
                if (t == 1) smallGlint[i] = 255;
                else if (t == 2) largeGlare[i] = 255;
            }
        }

        std::vector<uint8_t> work = imC;
        if (anySmall)
        {
            std::vector<uint8_t> dilS = vision::DilateRect3x3(smallGlint, w, h);
            std::vector<uint8_t> med7 = vision::MedianBlurU8(imC, w, h, 7);
            for (int i = 0; i < int(work.size()); i++)
                if (dilS[i] != 0) work[i] = med7[i];
        }
        if (anyLarge)
        {
            std::vector<uint8_t> dilL = vision::DilateRect3x3(largeGlare, w, h);
            std::vector<uint8_t> med11 = vision::MedianBlurU8(work, w, h, 11);
            for (int i = 0; i < int(work.size()); i++)
                if (dilL[i] != 0) work[i] = med11[i];
        }

        std::vector<uint8_t> med5Pre = vision::MedianBlurU8(work, w, h, 5);
        std::vector<float> glareFloat(w * h);
        for (int i = 0; i < int(work.size()); i++)
        {
            bool isSpike = work[i] > med5Pre[i] + 26;
            if (isSpike) work[i] = med5Pre[i];
            glareFloat[i] = (smallGlint[i] != 0 ? 0.45f : 0.f)
                          + (largeGlare[i] != 0 ? 1.50f : 0.f)
                          + (isSpike ? 0.25f : 0.f);
        }
        std::vector<float> glareMap = vision::GaussianBlurF32(glareFloat, w, h, 2.4);

        std::vector<uint8_t> med3U8 = vision::MedianBlurU8(work, w, h, 3);
        std::vector<uint8_t> med5U8 = vision::MedianBlurU8(work, w, h, 5);
        std::vector<float> clCap(w * h);
        std::vector<float> hetRaw(w * h);
        for (int i = 0; i < int(clCap.size()); i++)
        {
            float c3 = med3U8[i], c5 = med5U8[i];
            float cMix = 0.50f * c3 + 0.50f * c5;
            float sparkle = imC[i] - c5 - 12.0f;
            hetRaw[i] = std::abs(c3 - c5) + (sparkle > 0.f ? 0.35f * sparkle : 0.f);
            float lim = bgPre[i] + 14.0f;
            clCap[i] = cMix < lim ? cMix : lim;
        }

        std::vector<float> hetMap = vision::GaussianBlurF32(hetRaw, w, h, 1.2);
        int wH2 = std::max(16, w / 2), hH2 = std::max(16, h / 2);
        std::vector<float> cH2 = vision::ResizeAreaF32(clCap, w, h, wH2, hH2);
        std::vector<float> closedH2 = vision::MorphCloseEllipseF32(cH2, wH2, hH2, 33);
        std::vector<float> blurH2 = vision::GaussianBlurF32(closedH2, wH2, hH2, 33.0 * 0.85);
        std::vector<float> bgFull = vision::ResizeBilinearF32(blurH2, wH2, hH2, w, h);

        std::vector<float> diff(w * h);
        std::vector<float> posDiff(w * h);
        for (int i = 0; i < int(diff.size()); i++)
        {
            float d = bgFull[i] - clCap[i];
            diff[i] = d;
            posDiff[i] = d > 0.f ? d : 0.f;
        }
        auto tS1 = Clock::now();

        // S2: 连通域分割 + 码芯 [P16, P84] 归一化 + 保平顶截断高斯差分 (DoG)
        std::vector<float> uRough = vision::GaussianBlurF32(posDiff, w, h, 3.6);
        std::vector<uint8_t> uRoughU8(w * h);
        for (int i = 0; i < int(uRough.size()); i++)
            uRoughU8[i] = (uint8_t)std::clamp((int)(uRough[i] * 3.0f), 0, 255);
        double otsuTh = vision::ComputeOtsuThreshold(uRoughU8);

        std::vector<uint8_t> coreCand(w * h);
        float coreCut = (float)(otsuTh * 0.85);
        for (int i = 0; i < int(uRough.size()); i++)
            if (uRough[i] * 3.0f > coreCut) coreCand[i] = 255;

        auto [coreLabels, coreComps] = vision::ConnectedComponents8(coreCand, w, h, true);
        if (coreComps.empty()) {
            EnhancementResult result;
            result.image=gray.clone(); result.mode="no_core_found";
            result.s1Ms=result.enhanceMs=result.totalMs=elapsed(t0,tS1);
            return result;
        }

        auto bestCore = coreComps[0];
        for (int i = 1; i < int(coreComps.size()); i++)
            if (coreComps[i].Area > bestCore.Area) bestCore = coreComps[i];

        std::vector<float> coreDiffVals; coreDiffVals.reserve(bestCore.Area);
        for (int i = 0; i < int(coreLabels.size()); i++)
            if (coreLabels[i] == bestCore.Label) coreDiffVals.push_back(diff[i]);

        auto [p16, p84] = vision::ComputePercentiles16And84(coreDiffVals);
        float loNorm = p16 - 3.0f;
        float denomNorm = std::max(10.0f, (p84 + 6.0f) - loNorm);
        std::vector<float> u(w * h);
        std::vector<float> posU(w * h);
        for (int i = 0; i < int(u.size()); i++)
        {
            float v = (diff[i] - loNorm) / denomNorm;
            v = v < -0.05f ? -0.05f : (v > 1.05f ? 1.05f : v);
            u[i] = v;
            posU[i] = v > 0.f ? v : 0.f;
        }

        auto box = vision::MinAreaRectBox(bestCore.BoundaryPoints);
        int idxTl = 0, idxTr = 0, idxBr = 0, idxBl = 0;
        float minSum = INFINITY, maxSum = -INFINITY;
        float minDiff = INFINITY, maxDiff = -INFINITY;
        for (int i = 0; i < 4; i++)
        {
            float s = box[i].X + box[i].Y;
            float d = box[i].Y - box[i].X; // np.diff([x, y]) = y - x
            if (s < minSum) { minSum = s; idxTl = i; }
            if (s > maxSum) { maxSum = s; idxBr = i; }
            if (d < minDiff) { minDiff = d; idxTr = i; }
            if (d > maxDiff) { maxDiff = d; idxBl = i; }
        }
        std::vector<vision::Point> ordered = { box[idxTl], box[idxTr], box[idxBr], box[idxBl] };

        float d01 = std::sqrt((ordered[1].X - ordered[0].X) * (ordered[1].X - ordered[0].X) + (ordered[1].Y - ordered[0].Y) * (ordered[1].Y - ordered[0].Y));
        float d03 = std::sqrt((ordered[3].X - ordered[0].X) * (ordered[3].X - ordered[0].X) + (ordered[3].Y - ordered[0].Y) * (ordered[3].Y - ordered[0].Y));
        float span = 0.5f * (d01 + d03);
        int wTmp = std::max(128, (int)std::nearbyint(span));

        std::vector<float> ordSrc = { ordered[0].X, ordered[0].Y, ordered[1].X, ordered[1].Y, ordered[2].X, ordered[2].Y, ordered[3].X, ordered[3].Y };
        std::vector<float> ordDst = { 0.f, 0.f, float(wTmp), 0.f, float(wTmp), float(wTmp), 0.f, float(wTmp) };
        std::vector<double> hTmp(9);
        vision::SolveHomography4Pts(ordSrc, ordDst, hTmp);
        std::vector<float> uTmp = vision::WarpPerspectiveF32(posU, w, h, hTmp, wTmp, wTmp);

        int sw0 = std::max(5, wTmp / 10);
        int bestK90 = 0;
        double bestLScore = -INFINITY;
        for (int k = 0; k < 4; k++)
        {
            std::vector<float> rot = vision::Rot90Ccw(uTmp, wTmp, k);
            double maxBot = -INFINITY, maxTop = -INFINITY;
            for (int r = wTmp - sw0; r < wTmp; r++)
            {
                double s = 0;
                for (int c = 0; c < wTmp; c++) s += rot[r * wTmp + c];
                if (s / wTmp > maxBot) maxBot = s / wTmp;
            }
            for (int r = 0; r < sw0; r++)
            {
                double s = 0;
                for (int c = 0; c < wTmp; c++) s += rot[r * wTmp + c];
                if (s / wTmp > maxTop) maxTop = s / wTmp;
            }
            double maxLeft = -INFINITY, maxRight = -INFINITY;
            for (int c = 0; c < sw0; c++)
            {
                double s = 0;
                for (int r = 0; r < wTmp; r++) s += rot[r * wTmp + c];
                if (s / wTmp > maxLeft) maxLeft = s / wTmp;
            }
            for (int c = wTmp - sw0; c < wTmp; c++)
            {
                double s = 0;
                for (int r = 0; r < wTmp; r++) s += rot[r * wTmp + c];
                if (s / wTmp > maxRight) maxRight = s / wTmp;
            }
            double score = maxBot + maxLeft - maxTop - maxRight;
            if (score > bestLScore) { bestLScore = score; bestK90 = k; }
        }

        std::vector<float> cornersOuter(8);
        for (int i = 0; i < 4; i++)
        {
            auto pt = ordered[(i + bestK90) % 4];
            cornersOuter[2 * i] = pt.X;
            cornersOuter[2 * i + 1] = pt.Y;
        }
        float modPx = span / 32.0f;

        std::vector<float> u1Clk = vision::GaussianBlurF32(u, w, h, 0.17 * modPx);
        std::vector<float> g2Clk = vision::GaussianBlurF32(u, w, h, 0.58 * modPx);
        std::vector<float> filtClk(w * h);
        for (int i = 0; i < int(filtClk.size()); i++)
            filtClk[i] = u1Clk[i] + 2.4f * (u1Clk[i] - g2Clk[i]);

        std::vector<float> u1Fd1 = vision::GaussianBlurF32(u, w, h, 0.175 * modPx);
        std::vector<float> g2Fd1 = vision::GaussianBlurF32(u, w, h, 0.605 * modPx);
        std::vector<float> fd1(w * h);
        for (int i = 0; i < int(fd1.size()); i++)
        {
            float v = u1Fd1[i] + 2.6f * (u1Fd1[i] - g2Fd1[i]);
            fd1[i] = v < -0.15f ? -0.15f : (v > 1.12f ? 1.12f : v);
        }
        auto tS2 = Clock::now();

        // S3: 正交 8-DOF 亚像素时钟边锁定
        std::vector<double> hAsym(9);
        vision::SolveHomography4Pts(SrcAsym, cornersOuter, hAsym);
        std::vector<float> cInit(8);
        vision::PerspectiveTransformPts(SrcCorners, hAsym, cInit);
        std::vector<float> dstRaw = vision::OptimizeClock8Dof(filtClk, h, w, 32, cInit, modPx);
        auto tS3 = Clock::now();

        // S4: 惰性多带宽评估 + 二元域 GF(2) OSD 软判决恢复
        std::vector<double> hm1(9);
        vision::SolveHomography4Pts(SrcOb1, dstRaw, hm1);
        std::vector<float> pr1(32 * 32 * 2);
        vision::PerspectiveTransformPts(GridPts32, hm1, pr1);

        std::vector<float> hetGrid1 = vision::RemapGrid32(hetMap, w, h, pr1);
        std::vector<float> v1 = vision::RemapGrid32(fd1, w, h, pr1);

        std::vector<float> glareGrid1 = vision::RemapGrid32(glareMap, w, h, pr1);
        std::vector<float> wH1(784);
        std::vector<float> llrPrimary(784);
        for (int k = 0; k < 98; k++)
        {
            for (int b = 0; b < 8; b++)
            {
                int idx32 = ecc::Get().BitR32[k][b] * 32 + ecc::Get().BitC32[k][b];
                int bitIdx = k * 8 + b;
                float weight = 1.0f / ((1.0f + 0.09f * hetGrid1[idx32]) * (1.0f + 6.0f * glareGrid1[idx32]));
                wH1[bitIdx] = weight;
                llrPrimary[bitIdx] = (v1[idx32] - 0.50f) * weight;
            }
        }

        std::vector<uint8_t> msgOut(98);
        std::vector<uint8_t> wonBits32;
        std::string wonMode = "unsolved";

        int ordOut, bdOut, bitOut;
        int ret = ecc::DecodeBinaryOsd(llrPrimary, 62, 0, msgOut, ordOut, bdOut, bitOut);
        if (ret >= 0)
        {
            wonBits32 = ecc::Get().CodewordsToBits32(msgOut);
            wonMode = "OSD-" + std::to_string(ordOut) + "(fd1,bd=" + std::to_string(bdOut) + ")";
        }
        else
        {
            std::vector<float> u1Fd3 = vision::GaussianBlurF32(u, w, h, 0.255 * modPx);
            std::vector<float> g2Fd3 = vision::GaussianBlurF32(u, w, h, 0.66 * modPx);
            std::vector<float> fd3(w * h);
            for (int i = 0; i < int(fd3.size()); i++)
            {
                float v = u1Fd3[i] + 3.8f * (u1Fd3[i] - g2Fd3[i]);
                fd3[i] = v < -0.15f ? -0.15f : (v > 1.12f ? 1.12f : v);
            }

            std::vector<float> imgPts(8);
            vision::PerspectiveTransformPts(SrcCorners, hm1, imgPts);
            std::vector<double> hRect(9);
            vision::SolveHomography4Pts(imgPts, DstRect, hRect);
            std::vector<float> uRect = vision::WarpPerspectiveF32(posU, w, h, hRect, ecc::WRect, ecc::WRect);
            std::vector<float> mp2 = ecc::Get().SolveKroneckerPsf32(uRect);
            std::vector<float> vFd3 = vision::RemapGrid32(fd3, w, h, pr1);

            std::vector<float> llrEns(784);
            std::vector<float> llrFd46(784);
            for (int k = 0; k < 98; k++)
            {
                for (int b = 0; b < 8; b++)
                {
                    int idx32 = ecc::Get().BitR32[k][b] * 32 + ecc::Get().BitC32[k][b];
                    int bitIdx = k * 8 + b;
                    float ensVal = 0.5f * vFd3[idx32] + 0.5f * mp2[idx32];
                    llrEns[bitIdx] = (ensVal - 0.48f) * wH1[bitIdx];
                    llrFd46[bitIdx] = (v1[idx32] - 0.46f) * wH1[bitIdx];
                }
            }

            std::vector<double> hm2(9);
            vision::SolveHomography4Pts(SrcOb2, dstRaw, hm2);
            std::vector<float> pr2(32 * 32 * 2);
            vision::PerspectiveTransformPts(GridPts32, hm2, pr2);
            std::vector<float> hetGrid2 = vision::RemapGrid32(hetMap, w, h, pr2);
            std::vector<float> glareGrid2 = vision::RemapGrid32(glareMap, w, h, pr2);
            std::vector<float> v2Fd1 = vision::RemapGrid32(fd1, w, h, pr2);
            std::vector<float> llrOb2(784);
            for (int k = 0; k < 98; k++)
            {
                for (int b = 0; b < 8; b++)
                {
                    int idx32 = ecc::Get().BitR32[k][b] * 32 + ecc::Get().BitC32[k][b];
                    int bitIdx = k * 8 + b;
                    llrOb2[bitIdx] = (v2Fd1[idx32] - 0.48f) / ((1.0f + 0.09f * hetGrid2[idx32]) * (1.0f + 6.0f * glareGrid2[idx32]));
                }
            }

            std::vector<std::pair<std::string, std::vector<float>>> candidates =
            {
                {"ens3", llrEns},
                {"fd1_46", llrFd46},
                {"ob2_fd1", llrOb2},
                {"fd1_50", llrPrimary}
            };

            for (int tail : {0, 200})
            {
                for (const auto& [cName, llrC] : candidates)
                {
                    if (tail == 0 && cName == "fd1_50") continue;
                    ret = ecc::DecodeBinaryOsd(llrC, 66, tail, msgOut, ordOut, bdOut, bitOut);
                    if (ret >= 0)
                    {
                        wonBits32 = ecc::Get().CodewordsToBits32(msgOut);
                        wonMode = "OSD-" + std::to_string(ordOut) + "(" + cName + ",bd=" + std::to_string(bdOut) + ")";
                        break;
                    }
                }
                if (!wonBits32.empty()) break;
            }
        }

        if (wonBits32.empty())
        {
            wonBits32 = std::vector<uint8_t>(1024);
            for (int i = 0; i < 1024; i++) wonBits32[i] = v1[i] > 0.50f ? (uint8_t)1 : (uint8_t)0;
            ecc::Tables::ApplyEcc200Clock32(wonBits32);
        }

        // 渲染带标准白色静区的二值 DataMatrix 图像，交给 ZXing-C++ 解码。
        int symW = 32 * upscaleModulePx;
        int outW = symW + 2 * quietZonePx;
        std::vector<uint8_t> enhImg(outW * outW);
        for (int i = 0; i < int(enhImg.size()); i++) enhImg[i] = 255;
        for (int r = 0; r < 32; r++)
        {
            for (int c = 0; c < 32; c++)
            {
                if (wonBits32[r * 32 + c] == 0) continue;
                int y0 = quietZonePx + r * upscaleModulePx;
                int x0 = quietZonePx + c * upscaleModulePx;
                for (int dy = 0; dy < upscaleModulePx; dy++)
                {
                    int rOff = (y0 + dy) * outW + x0;
                    for (int dx = 0; dx < upscaleModulePx; dx++)
                        enhImg[rOff + dx] = 0;
                }
            }
        }

    auto tS4=Clock::now();
    EnhancementResult result;
    result.image=cv::Mat(outW,outW,CV_8UC1,enhImg.data()).clone();
    result.text=decode(result.image);
    auto tS5=Clock::now();
    result.mode=wonMode; result.pixelsPerModule=modPx;
    result.s1Ms=elapsed(t0,tS1); result.s2Ms=elapsed(tS1,tS2);
    result.s3Ms=elapsed(tS2,tS3); result.s4Ms=elapsed(tS3,tS4);
    result.enhanceMs=elapsed(t0,tS4); result.decodeMs=elapsed(tS4,tS5); result.totalMs=elapsed(t0,tS5);
    return result;
}
}}}
