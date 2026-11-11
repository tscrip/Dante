#pragma once
#include <Arduino.h>
#include <ArduinoJson.h>

// Line-oriented JSON link over Bluetooth LE (Nordic UART Service) and USB serial.
// Replies go out on whichever link last delivered a message.
namespace Link {
  typedef void (*Handler)(JsonDocument& msg);

  void begin(Handler handler);
  void poll();                       // read both links, dispatch complete lines
  void send(JsonDocument& msg);      // serialize + '\n' on the active link
  bool pcAlive();                    // heard from the PC recently?
  bool btConnected();                // a BLE central is connected
  uint32_t lastHeardMs();
}
