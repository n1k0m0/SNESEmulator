// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy at https://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE and NOTICE for permissions and attribution.
//
// Supplies deterministic replay inputs to the external whole-console reference adapters.
//

// Own deterministic laboratory input plan: zero-based raw field, JOY1 mask.
#pragma once
#include <fstream>
#include <vector>
#include <string>
struct ReplayPlan {
  std::vector<std::pair<unsigned,unsigned>> events;
  void load(const char* path){std::ifstream f(path);unsigned field,mask;while(f>>field>>mask)events.emplace_back(field,mask);if(!f.eof())throw std::runtime_error("Invalid replay plan");}
  unsigned mask(unsigned field)const{unsigned result=0;for(auto e:events)if(e.first<=field)result=e.second;return result;}
};
