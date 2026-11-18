#pragma once
#include <set>
#include "Arduino.h"
// I2C stand-in: transfers go nowhere (the simulator reads the display buffers directly), but
// address probes answer for the simulated hardware, so the firmware's detection runs for real.
class TwoWire {
 public:
  std::set<uint8_t> present;     // addresses that answer on this bus
  bool begin(int = -1, int = -1, uint32_t = 0) { return true; }
  void setClock(uint32_t) {}
  void beginTransmission(uint8_t a) { addr_ = a; }
  uint8_t endTransmission(bool = true) { return present.count(addr_) ? 0 : 2; }
  size_t write(uint8_t) { return 1; }
  size_t write(const uint8_t*, size_t n) { return n; }
 private:
  uint8_t addr_ = 0;
};
extern TwoWire Wire;
extern TwoWire Wire1;
