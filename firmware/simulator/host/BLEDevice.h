#pragma once
// Bluetooth LE stand-in: the simulator talks to the PC app over stdin/stdout instead,
// so no central ever connects.
#include <string>
#include "Arduino.h"
class BLEServer;
class BLECharacteristic;
struct BLEServerCallbacks { virtual ~BLEServerCallbacks() {} virtual void onConnect(BLEServer*) {} virtual void onDisconnect(BLEServer*) {} };
struct BLECharacteristicCallbacks { virtual ~BLECharacteristicCallbacks() {} virtual void onWrite(BLECharacteristic*) {} };
class BLEDescriptor { public: virtual ~BLEDescriptor() {} };
class BLECharacteristic {
 public:
  static const uint32_t PROPERTY_READ = 1, PROPERTY_WRITE = 2, PROPERTY_NOTIFY = 4, PROPERTY_WRITE_NR = 8;
  void addDescriptor(BLEDescriptor*) {}
  void setCallbacks(BLECharacteristicCallbacks*) {}
  std::string getValue() { return ""; }
  void setValue(uint8_t*, size_t) {}
  void notify() {}
};
class BLEService {
 public:
  BLECharacteristic* createCharacteristic(const char*, uint32_t) { return new BLECharacteristic(); }
  void start() {}
};
class BLEServer {
 public:
  void setCallbacks(BLEServerCallbacks*) {}
  BLEService* createService(const char*) { return new BLEService(); }
  void startAdvertising() {}
};
class BLEAdvertising { public: void addServiceUUID(const char*) {} void setScanResponse(bool) {} };
class BLEDevice {
 public:
  static void init(const char*) {}
  static void setMTU(int) {}
  static BLEServer* createServer() { return new BLEServer(); }
  static BLEAdvertising* getAdvertising() { static BLEAdvertising a; return &a; }
  static void startAdvertising() {}
};
