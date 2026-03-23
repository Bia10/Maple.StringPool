# StringPoolLib — Reverse Engineering References

All native addresses, PDB metadata, and source-level cross-references for the GMS v95
`StringPool` implementation. Previously embedded in XML doc comments; kept here to leave
the C# API docs clean.

All addresses are relative to GMS v95 `MapleStory.exe` (image base `0x400000`).
Addresses were validated against the official GMS v95 PDB leak (`index.json`).

---

## PDB Type Metadata

| C# Type | Native Type | PDB Ordinal |
|---------|-------------|-------------|
| `StringPoolDecoder` | `StringPool` | 8089 (`sizeof = 0x10`) |
| `RotatedKey` | `StringPool::Key` | 8091 |

---

## Function Addresses

### StringPool

| Function | Address | Description |
|----------|---------|-------------|
| `StringPool::GetInstance()` | `0x7466A0` | Singleton accessor |
| `StringPool::StringPool()` | `0x7465D0` | Constructor; calls `ZArray<ZXString<char>*>::_Alloc(0x1AE3)` |
| `StringPool::GetString(uint)` | `0x403B30` | Public narrow-string accessor |
| `StringPool::GetString(uint, char)` | `0x746750` | Private lazy-init + decode |
| `StringPool::GetStringW(uint)` | `0x403B60` | Wide-string accessor |
| `StringPool::GetString(uint, unsigned short)` | `0x746880` | Private wide decode |
| `StringPool::GetBSTR(uint)` | `0x404BB0` | BSTR wrapper |

### StringPool::Key

| Function | Address | Description |
|----------|---------|-------------|
| `StringPool::Key::Key(pKey, nKeySize, nSeed)` | `0x746470` | Key constructor; allocates `ZArray<unsigned char>`, copies `ms_aKey`, calls `rotatel` |
| `StringPool::Key::GetKey(uint)` | `0x746230` | Returns `m_aKey[nIdx % keySize]` |

### Crypto Templates

| Function | Address | Description |
|----------|---------|-------------|
| `rotatel<unsigned char>` | `0x746270` | Circular left-rotation template |
| `anonymous::Decode<char>` | `0x746520` | XOR decode template; implements zero-collision rule |

---

## Static `.data` Addresses (GMS v95)

| Field | Address | Type | Value |
|-------|---------|------|-------|
| `StringPool::ms_aString` | `0xC5A878` | `const char*[6883]` | Pointer table |
| `StringPool::ms_aKey` | `0xB98830` | `const unsigned char[16]` | XOR master key |
| `StringPool::ms_nKeySize` | `0xB98840` | `const uint` | 16 |
| `StringPool::ms_nSize` | `0xB98844` | `const uint` | 6883 (`0x1AE3`) |
| `StringPool::ms_pInstance` | `0xC6E6E0` | `StringPool*` | Singleton pointer |
| `ClassLevelLockable<StringPool>::ms_nLocker` | `0xC6E6E4` | `volatile LONG` | Spinlock |

---

## Native Type Cross-References (`game_types.h`)

| C# Type | Native Type | Line |
|---------|-------------|------|
| `StringPoolLayout` | `StringPool` | 67186 |
| `StringPoolLayout` (base) | `ClassLevelLockable<StringPool>` | 67172 |
| `StringPoolKeyLayout` | `StringPool::Key` | 67197 |
| `ZXStringDataLayout` | `ZXString<char>::_ZXStringData` | 38554 |
| `ZXStringDataLayout` (wide) | `ZXString<unsigned short>::_ZXStringData` | 56548 |
| `ZXString` | `ZXString<char>` | 19418 |
| `ZFatalSectionLayout` | `ZFatalSectionData` | 17668 |
| `ZFatalSection` (derived) | `ZFatalSection` | 17674 |
| `ZArrayLayout` | `ZArray<T>` | various |

---

## Native Struct Layouts

### `StringPool` (`sizeof = 0x10`)
```cpp
struct StringPool : ClassLevelLockable<StringPool>
{
    ZArray<ZXString<char> *>           m_apZMString;  // +0x00
    ZArray<ZXString<unsigned short> *> m_apZWString;  // +0x04
    ZFatalSection                      m_lock;        // +0x08
};
```

### `StringPool::Key`
```cpp
struct StringPool::Key {
    ZArray<unsigned char> m_aKey;   // +0x00
};
```

### `ZFatalSectionData`
```cpp
struct ZFatalSectionData {
    void *_m_pTIB;   // +0x00  thread information block pointer
    int   _m_nRef;   // +0x04  reentrance count
};
```

### `ZXString<char>::_ZXStringData`
```cpp
struct _ZXStringData {
    int nRef;       // +0x00  reference count
    int nCap;       // +0x04  allocated capacity
    int nByteLen;   // +0x08  payload byte length (excluding null terminator)
};
// immediately followed by char[] payload + null terminator
```

### `ZXString<char>`
```cpp
struct ZXString<char> {
    char *_m_pStr;   // +0x00  → points at payload (past _ZXStringData header)
};
```

### `ZArray<T>`
```cpp
struct ZArray<T> {
    T *a;   // +0x00  → points past count header
};
// Allocation: [int32 count][T[0], T[1], ...]
// The `a` pointer points at T[0]; count lives at `a - sizeof(int)`.
```
