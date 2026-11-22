#pragma once
#include <map>
#include <string>
#include "Arduino.h"
// In-memory stand-in for the ESP32 NVS key/value store (the PC app re-sends settings on connect).
// Like the real thing, opening a namespace read-only fails until something was written to it.
class Preferences {
  using Ns = std::map<std::string, int>;
  using StrNs = std::map<std::string, std::string>;
  static std::map<std::string, Ns>& all() { static std::map<std::string, Ns> s; return s; }
  static std::map<std::string, StrNs>& strings() { static std::map<std::string, StrNs> s; return s; }
  std::string ns_;
 public:
  bool begin(const char* name, bool readOnly = false) {
    if (readOnly && !all().count(name)) return false;
    ns_ = name; all()[ns_]; return true;
  }
  void end() {}
  uint8_t getUChar(const char* k, uint8_t d) { auto& m = all()[ns_]; auto it = m.find(k); return it == m.end() ? d : (uint8_t)it->second; }
  bool getBool(const char* k, bool d) { auto& m = all()[ns_]; auto it = m.find(k); return it == m.end() ? d : it->second != 0; }
  size_t putUChar(const char* k, uint8_t v) { all()[ns_][k] = v; return 1; }
  size_t putBool(const char* k, bool v) { all()[ns_][k] = v; return 1; }
  uint32_t getUInt(const char* k, uint32_t d) { auto& m = all()[ns_]; auto it = m.find(k); return it == m.end() ? d : (uint32_t)it->second; }
  size_t putUInt(const char* k, uint32_t v) { all()[ns_][k] = (int)v; return 4; }
  // Strings, with the real API's shape: get copies into a buffer and returns the length (0 if missing).
  size_t putString(const char* k, const char* v) { strings()[ns_][k] = v; all()[ns_][k] = 0; return strlen(v); }
  size_t getString(const char* k, char* out, size_t max) {
    auto& m = strings()[ns_]; auto it = m.find(k);
    if (it == m.end() || it->second.size() + 1 > max) return 0;
    memcpy(out, it->second.c_str(), it->second.size() + 1);
    return it->second.size();
  }
  bool isKey(const char* k) { return all()[ns_].count(k) != 0; }
  bool remove(const char* k) { strings()[ns_].erase(k); return all()[ns_].erase(k) != 0; }
};
