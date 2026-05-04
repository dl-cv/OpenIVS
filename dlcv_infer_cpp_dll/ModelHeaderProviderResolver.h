#pragma once

#include <string>
#include "dlcv_sntl_admin.h"

namespace sntl_admin {
    DogProvider ResolveModelHeaderProvider(const std::wstring& modelPath);
    DogProvider ResolveModelHeaderProvider(const std::string& modelPathUtf8OrGbk);
}
