#pragma once

#include <string>

namespace dlcv_test {

int LoadOwnedModel(const std::wstring& modelPath, int deviceId);
bool ReleaseOwnedModel(int modelIndex) noexcept;
std::string GetBorrowedModelInfoResult(int modelIndex);
int QueryLoadedSharedIndexType(int modelIndex);
int RunSharedIndexResolverSelfTest();
int RunFlowModelIndexRulesSelfTest();

} // namespace dlcv_test
