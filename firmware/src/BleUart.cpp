#include "BleUart.h"
#include <atomic>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLE2902.h>

namespace {
const char* SERVICE_UUID = "6E400001-B5A3-F393-E0A9-E50E24DCCA9E";
const char* RX_UUID      = "6E400002-B5A3-F393-E0A9-E50E24DCCA9E";
const char* TX_UUID      = "6E400003-B5A3-F393-E0A9-E50E24DCCA9E";

// Writes arrive on the Bluetooth task; the main loop drains them. Single producer /
// single consumer ring buffer, so two atomic indexes are all the locking needed.
const uint16_t RING = 2048;
uint8_t ring[RING];
std::atomic<uint16_t> head{0}, tail{0};

BLEServer* server = nullptr;
BLECharacteristic* tx = nullptr;
std::atomic<bool> isConnected{false};

// Keep notifications within the smallest ATT payload every central supports (MTU 23 - 3).
// Our device->PC messages are short, so this costs little and never gets truncated.
const size_t CHUNK = 20;

class ServerCallbacks : public BLEServerCallbacks {
  void onConnect(BLEServer*) override { isConnected = true; }
  void onDisconnect(BLEServer* s) override {
    isConnected = false;
    s->startAdvertising();   // be discoverable again straight away
  }
};

class RxCallbacks : public BLECharacteristicCallbacks {
  void onWrite(BLECharacteristic* c) override {
    auto value = c->getValue();            // std::string (core 2.x) or String (core 3.x)
    const char* data = value.c_str();
    size_t len = value.length();
    for (size_t i = 0; i < len; i++) {
      uint16_t h = head.load();
      uint16_t next = (uint16_t)((h + 1) % RING);
      if (next == tail.load()) break;      // full: drop (the PC re-sends state on reconnect)
      ring[h] = (uint8_t)data[i];
      head.store(next);
    }
  }
};
} // namespace

namespace BleUart {

void begin(const char* name) {
  BLEDevice::init(name);
  BLEDevice::setMTU(517);
  server = BLEDevice::createServer();
  server->setCallbacks(new ServerCallbacks());

  BLEService* service = server->createService(SERVICE_UUID);
  tx = service->createCharacteristic(TX_UUID, BLECharacteristic::PROPERTY_NOTIFY);
  tx->addDescriptor(new BLE2902());
  BLECharacteristic* rx = service->createCharacteristic(
      RX_UUID, BLECharacteristic::PROPERTY_WRITE | BLECharacteristic::PROPERTY_WRITE_NR);
  rx->setCallbacks(new RxCallbacks());
  service->start();

  BLEAdvertising* adv = BLEDevice::getAdvertising();
  adv->addServiceUUID(SERVICE_UUID);
  adv->setScanResponse(true);   // the name goes in the scan response
  BLEDevice::startAdvertising();
}

bool connected() { return isConnected.load(); }

int available() {
  return (int)((head.load() + RING - tail.load()) % RING);
}

int read() {
  uint16_t t = tail.load();
  if (t == head.load()) return -1;
  uint8_t b = ring[t];
  tail.store((uint16_t)((t + 1) % RING));
  return b;
}

void write(const uint8_t* data, size_t len) {
  if (!tx || !connected()) return;
  for (size_t off = 0; off < len; off += CHUNK) {
    size_t n = len - off < CHUNK ? len - off : CHUNK;
    tx->setValue((uint8_t*)(data + off), n);
    tx->notify();
    delay(3);   // give the stack room so back-to-back notifications aren't dropped
  }
}

} // namespace BleUart
