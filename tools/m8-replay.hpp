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
