#pragma once

#include <string>

#include "dlcv_infer.h"

extern dlcv_infer::Model global_model;

void LoadGlobalModel(const std::string& model_path, int device_id);
void InferTest(const std::string& img_path);
