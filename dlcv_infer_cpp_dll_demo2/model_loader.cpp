#include "demo2_api.h"

#include <utility>

void LoadGlobalModel(const std::string& model_path, int device_id) {
    dlcv_infer::Model loaded_model(model_path, device_id);
    global_model = std::move(loaded_model);
}
