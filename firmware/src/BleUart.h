#pragma once
#include <Arduino.h>

// "Serial over Bluetooth LE" using the Nordic UART Service (NUS), the de-facto standard
// that most BLE terminal apps and libraries speak. The ESP32-S3 has no Bluetooth Classic,
// so this replaces the old SPP link.
//   service 6E400001-B5A3-F393-E0A9-E50E24DCCA9E
//   RX      6E400002-...  (PC writes here)
//   TX      6E400003-...  (DANTE notifies here)
namespace BleUart {
  void begin(const char* name);
  bool connected();
  int available();
  int read();                                   // -1 if nothing buffered
  void write(const uint8_t* data, size_t len);  // chunks into notifications
}
