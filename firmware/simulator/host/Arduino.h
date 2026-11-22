#pragma once
// DANTE simulator: a small stand-in for the Arduino core, so the real firmware runs on a PC.
// Only the simulator build puts this folder on the include path; the ESP32 build never sees it.
// Some libraries include it from C files too, so the top part is plain C.
#include <stdint.h>
#include <stddef.h>
#include <string.h>

// Program-memory helpers (on a PC everything is just memory).
#define PROGMEM
#define PSTR(s) (s)
#define memcpy_P memcpy
#define memcmp_P memcmp
#define strcmp_P strcmp
#define strncpy_P strncpy
#define strlen_P strlen
#define pgm_read_byte(p) (*(const uint8_t*)(p))
#define pgm_read_word(p) (*(const uint16_t*)(p))
#define pgm_read_dword(p) (*(const uint32_t*)(p))
#define pgm_read_ptr(p) (*(void* const*)(p))

#ifdef __cplusplus
#include <cstdint>
#include <cstring>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <string>
#include <deque>
#include <algorithm>
#include <strings.h>
using std::min; using std::max;
typedef uint8_t byte;
typedef bool boolean;

uint32_t millis();
uint32_t micros();
void delay(uint32_t ms);
void delayMicroseconds(uint32_t us);
uint32_t esp_random();

// Memory: the simulated S3 has plenty of "PSRAM", so the TFT runs its full-frame mode; the simulated
// yellow board has none, like the real one.
#define MALLOC_CAP_SPIRAM   (1 << 10)
#define MALLOC_CAP_DMA      (1 << 3)
#define MALLOC_CAP_INTERNAL (1 << 11)
extern bool g_simPsram;   // false when pretending to be the yellow board (no PSRAM: the TFT draws in strips)
inline bool psramFound() { return g_simPsram; }
inline void* heap_caps_malloc(size_t n, uint32_t) { return malloc(n); }
inline void heap_caps_free(void* p) { free(p); }
inline long random(long a, long b) { return b <= a ? a : a + (long)(esp_random() % (uint32_t)(b - a)); }
inline long random(long b) { return random(0, b); }
inline void randomSeed(unsigned long) {}
#define constrain(x, lo, hi) ((x) < (lo) ? (lo) : ((x) > (hi) ? (hi) : (x)))
#define DEG_TO_RAD 0.017453292519943295f
#define INPUT 0
#define OUTPUT 1
#define INPUT_PULLUP 2
#define LOW 0
#define HIGH 1
#define F(x) x
#define MSBFIRST 1

// Buttons: the simulator front end (the PC app) drives these.
extern int g_simPins[64];
inline void pinMode(int, int) {}
inline int digitalRead(int p) { return (p >= 0 && p < 64) ? g_simPins[p] : HIGH; }
inline void digitalWrite(int, int) {}
inline void yield() {}

// Enough of Arduino's String for libraries that offer String overloads.
class String {
 public:
  String(const char* s = "") : s_(s ? s : "") {}
  String(const std::string& s) : s_(s) {}
  const char* c_str() const { return s_.c_str(); }
  size_t length() const { return s_.size(); }
 private:
  std::string s_;
};

// SPI as newer Arduino cores declare it in Arduino.h. The simulator never talks to a real bus:
// the TFT's pixels are taken from its frame buffer instead.
#define SPI_MODE0 0
#define SPI_MODE1 1
#define SPI_MODE2 2
#define SPI_MODE3 3
struct SPISettings { SPISettings(uint32_t = 0, int = 0, int = 0) {} };
class HardwareSPI {
 public:
  explicit HardwareSPI(int bus = 0) { (void)bus; }
  void begin(int = -1, int = -1, int = -1, int = -1) {}
  void end() {}
  void beginTransaction(SPISettings) {}
  void endTransaction() {}
  uint8_t transfer(uint8_t) { return 0; }
  void transfer(void*, size_t) {}
};

class Print {
 public:
  virtual ~Print() {}
  virtual size_t write(uint8_t c) = 0;
  virtual size_t write(const uint8_t* b, size_t n) { for (size_t i = 0; i < n; i++) write(b[i]); return n; }
  size_t write(const char* s) { return write((const uint8_t*)s, strlen(s)); }
  size_t print(const char* s) { return write(s); }
  size_t println(const char* s) { return write(s) + write((const uint8_t*)"\r\n", 2); }
};

class Stream : public Print {
 public:
  virtual int available() = 0;
  virtual int read() = 0;
  using Print::write;
  // Reads what's there, up to n bytes (no waiting: nothing in the simulator streams in slowly).
  size_t readBytes(uint8_t* buf, size_t n) {
    size_t got = 0;
    for (int c; got < n && (c = read()) >= 0;) buf[got++] = (uint8_t)c;
    return got;
  }
  size_t readBytes(char* buf, size_t n) { return readBytes((uint8_t*)buf, n); }
};

// `Serial` = the simulator's stdin/stdout (what the PC app talks to). `Serial0` = unused.
class HardwareSerial : public Stream {
 public:
  explicit HardwareSerial(bool stdio) : stdio_(stdio) {}
  void begin(unsigned long) {}
  int available() override;
  int read() override;
  size_t write(uint8_t c) override;
  size_t write(const uint8_t* b, size_t n) override;
  using Print::write;
 private:
  bool stdio_;
};
extern HardwareSerial Serial;
extern HardwareSerial Serial0;

#endif // __cplusplus
