#include <math.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#if defined(_MSC_VER)
#include <intrin.h>

static inline int low64(unsigned long long v) {
  unsigned long p;
  _BitScanForward64(&p, v);
  return (int)p;
}
#else

static inline int low64(unsigned long long v) { return __builtin_ctzll(v); }
#endif
#define N 784
#define M 288
#define W 5
typedef unsigned long long U;
typedef struct {
  U a[W];
} V;
typedef struct {
  float val;
  int idx;
} Item;
static int cmp(const void *a, const void *b) {
  const Item *p = a, *q = b;
  return (p->val > q->val) - (p->val < q->val);
}
static void add(V *a, const V *b) {
  for (int i = 0; i < W; i++)
    a->a[i] ^= b->a[i];
}
static void mark(V *a, int p) { a->a[p / 64] |= 1ull << (p % 64); }
static int bit(const V *a, int p) {
  return !!(a->a[p / 64] & (1ull << (p % 64)));
}
static int lead(const V *a) {
  for (int i = 0; i < W; i++)
    if (a->a[i]) {
      return i * 64 + low64(a->a[i]);
    }
  return -1;
}
static uint32_t rng = 1;
static float rand1(void) {
  rng ^= rng << 13;
  rng ^= rng >> 17;
  rng ^= rng << 5;
  return (rng + .5f) / 4294967296.f;
}
static V pc[N], syn, bestv, base, basis[M], repr[M], reps[500];
static int pivot[M], bestf[6], chosen[6], freeidx[500];
static float best;
static float absval[N], lut[36][256];
static void score(V v, float w, int n) {
  unsigned char *b = (unsigned char *)v.a;
  for (int i = 0; i < 36; i += 4) {
    w += lut[i][b[i]];
    w += lut[i + 1][b[i + 1]];
    w += lut[i + 2][b[i + 2]];
    w += lut[i + 3][b[i + 3]];
    if (w >= best) return;
  }
  best = w;
  bestv = v;
  for (int i = 0; i < 6; i++)
    bestf[i] = i < n ? chosen[i] : -1;
}

#define HS (1 << 18)
#define CAP 160000
static int sigbits;
static int head[HS], next[CAP], ent[CAP][3], nsig[500], sigpos[21], entries;
static int signa(V v) {
  int sig = 0;
  for (int i = 0; i < sigbits; i++)
    sig |= bit(&v, sigpos[i]) << i;
  return sig;
}
static void put(int sig, int a, int b, int c) {
  if (entries >= CAP) return;
  int i = entries++;
  next[i] = head[sig];
  head[sig] = i;
  ent[i][0] = a;
  ent[i][1] = b;
  ent[i][2] = c;
}
static void join(int sig, int a, int b, int c) {
  if (head[sig] < 0) return;
  const int right[3] = {a, b, c};
  int rightCount = 0;
  float rightWeight = 0;
  V rightValue = base;
  for (int j = 0; j < 3; j++) if (right[j] >= 0) {
    rightWeight += absval[freeidx[right[j]]];
    add(&rightValue, &reps[right[j]]);
    chosen[rightCount++] = freeidx[right[j]];
  }
  for (int e = head[sig]; e >= 0; e = next[e]) {
    float w = rightWeight;
    for (int j = 0; j < 3; j++) if (ent[e][j] >= 0) w += absval[freeidx[ent[e][j]]];
    if (w >= best) continue;
    V z = rightValue;
    int n = rightCount;
    for (int j = 0; j < 3; j++) {
      int k = ent[e][j];
      if (k >= 0) {
        add(&z, &reps[k]);
        chosen[n++] = freeidx[k];
      }
    }
    score(z, w, n);
  }
}
static void deeper(int f) {
  int seed = signa(base), mid = f / 2;
  for (int j = 0; j < f; j++)
    nsig[j] = signa(reps[j]);
  memset(head, 255, (1u << sigbits) * sizeof(*head));
  entries = 0;
  put(0, -1, -1, -1);
  for (int i = 0; i < mid; i++) {
    put(nsig[i], i, -1, -1);
    for (int j = 0; j < i; j++) {
      int s = nsig[i] ^ nsig[j];
      put(s, i, j, -1);
      for (int k = 0; i < 76 && k < j; k++)
        put(s ^ nsig[k], i, j, k);
    }
  }
  join(seed, -1, -1, -1);
  for (int i = mid; i < f; i++) {
    join(seed ^ nsig[i], i, -1, -1);
    for (int j = mid; j < i; j++) {
      int s = seed ^ nsig[i] ^ nsig[j];
      join(s, i, j, -1);
      for (int k = mid; i < mid+76 && k < j; k++)
        join(s ^ nsig[k], i, j, k);
    }
  }
}
static void Search(const U *par, const float *soft, int its, int k, int depth,
                uint32_t seed, unsigned char *out, float *cost) {
  if (!par || !soft || !out || !cost || its < 1 || its > 20000 || k < 2 ||
      k > 496 || (depth != 3 && depth != 6))
    return;
  sigbits = 17;
  memcpy(pc, par, sizeof pc);
  rng = seed;
  best = 1e9f;
  memset(&syn, 0, sizeof syn);
  for (int i = 0; i < N; i++) {
    absval[i] = fabsf(soft[i]);
    if (soft[i] < 0)
      add(&syn, &pc[i]);
  }
  Item order[N];
  for (int it = 0; it < its; it++) {
    float noise = (it % 6) * (.18f + .18f*((it/6)%3));
    for (int i = 0; i < N; i++) {
      order[i].idx = i;
      order[i].val =
          absval[i] *
          (it ? expf(noise * (rand1() + rand1() + rand1() - 1.5f)) : 1.f);
    }
    qsort(order, N, sizeof(Item), cmp);
    memset(basis, 0, sizeof basis);
    memset(repr, 0, sizeof repr);
    int p = 0, f = 0;
    for (int n = 0; n < N; n++) {
      int i = order[n].idx;
      V z = pc[i], r = {0};
      int j;
      while ((j = lead(&z)) >= 0 && bit(&basis[j], j)) {
        add(&z, &basis[j]);
        add(&r, &repr[j]);
      }
      if (j >= 0) {
        pivot[p] = i;
        mark(&r, p++);
        basis[j] = z;
        repr[j] = r;
      } else if (f < k) {
        reps[f] = r;
        freeidx[f++] = i;
      }
      if (p == M && f == k)
        break;
    }
    if (p != M) return;
    V z = syn, r = {0};
    int j;
    while ((j = lead(&z)) >= 0) {
      add(&z, &basis[j]);
      add(&r, &repr[j]);
    }
    base = r;
    for (int i = 0; i < 36; i++) {
      lut[i][0] = 0;
      for (int n = 1; n < 256; n++) {
        int l = low64((unsigned)n);
        lut[i][n] = lut[i][n & (n - 1)] + absval[pivot[i * 8 + l]];
      }
    }
    if (depth == 6) {
      Item positions[M];
      for (int i = 0; i < M; i++) {
        positions[i].idx = i;
        positions[i].val = -absval[pivot[i]] * (.5f + 1.4f * rand1());
      }
      qsort(positions, M, sizeof(Item), cmp);
      for (int i = 0; i < sigbits; i++)
        sigpos[i] = positions[i].idx;
      for (int i = f - 1; i > 0; i--) {
        int j = (int)(rand1() * i);
        V tmp = reps[j];
        reps[j] = reps[i];
        reps[i] = tmp;
        int t = freeidx[j];
        freeidx[j] = freeidx[i];
        freeidx[i] = t;
      }
      for (int h=0; h<2; ++h) {
        int start=h*(f/2),stop=h?f:f/2; Item ao[500]; V vr[500]; int vi[500];
        for(int j=start;j<stop;j++){ao[j-start].idx=j;ao[j-start].val=absval[freeidx[j]]*(.65f+.7f*rand1());}
        qsort(ao,stop-start,sizeof(Item),cmp);
        for(int j=start;j<stop;j++){int idx=ao[j-start].idx;vr[j]=reps[idx];vi[j]=freeidx[idx];}
        for(int j=start;j<stop;j++){reps[j]=vr[j];freeidx[j]=vi[j];}
      }
      deeper(f);
    } else {
      score(base, 0, 0);
      for (int i = 0; i < f; i++) {
        V a = base;
        add(&a, &reps[i]);
        float ca = absval[freeidx[i]];
        chosen[0] = freeidx[i];
        score(a, ca, 1);
        for (int j = 0; j < i; j++) {
          V b = a;
          add(&b, &reps[j]);
          float cb = ca + absval[freeidx[j]];
          chosen[1] = freeidx[j];
          score(b, cb, 2);
          if (depth >= 3)
            for (int l = 0; l < j; l++) {
              V c = b;
              add(&c, &reps[l]);
              float cc = cb + absval[freeidx[l]];
              chosen[2] = freeidx[l];
              score(c, cc, 3);
            }
        }
      }
    }
    if (best < *cost) {
      *cost = best;
      for (int i = 0; i < N; i++)
        out[i] = soft[i] < 0;
      for (int i = 0; i < M; i++)
        if (bit(&bestv, i))
          out[pivot[i]] ^= 1;
      for (int i = 0; i < 6; i++)
        if (bestf[i] >= 0)
          out[bestf[i]] ^= 1;
      int d = 0;
      for (int j = 0; j < N; j++)
        d += (out[j] != (soft[j] < 0));
      if (d <= 82)
        return;
    }
  }
}
