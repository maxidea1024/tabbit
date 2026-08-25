// Just enough of Unreal's core types to build and run the generated Unreal target off-engine.
//
// The Unreal target was the one output nobody checked the values of. Every other language has a
// conformance harness that reads the corpus and is compared field by field against what the
// exporter wrote; Unreal had only "does it compile, does it use engine types, does it avoid
// throwing" - because running it meant installing an engine. So the reader's varint decoding,
// its zig-zag, its UTF-8, its GUID byte order and its tick handling were the least checked bytes
// in the repository, in the target most likely to ship in a game.
//
// This is the same trade as test/fixtures/tools/cs-compile-check/UnityStubs.cs, one step further:
// those stubs only had to compile, and these have to work.
//
// What it does and does not prove
// -------------------------------
// The decoding is the generated code's and the reader's, and that is what the corpus compares.
// What is here is storage and formatting: FString holds characters, FGuid holds four integers,
// FDateTime holds ticks. The reader's job is to put the right values into them, and the harness
// reads them straight back out - so a wrong shift, a swapped byte or a mis-signed varint fails
// here exactly as it would in the engine.
//
// What it cannot prove is that the engine's own types behave as these do. If FGuid's components
// were laid out differently from what is assumed here, both this and the reader would be wrong
// together and this would still pass. That is a real limit and the reason this file states, for
// each type, the engine behaviour it is standing in for.
//
// Nothing here is a general-purpose Unreal. It implements the members the generated code and the
// reader name, and no others.

#pragma once

#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iostream>
#include <string>
#include <type_traits>
#include <unordered_map>
#include <initializer_list>
#include <vector>

// --------------------------------------------------------------------- integers

using int8 = std::int8_t;
using uint8 = std::uint8_t;
using int16 = std::int16_t;
using uint16 = std::uint16_t;
using int32 = std::int32_t;
using uint32 = std::uint32_t;
using int64 = std::int64_t;
using uint64 = std::uint64_t;

/// UE5 makes TCHAR char16_t on every platform, and UTF8CHAR a distinct type from ANSICHAR.
using TCHAR = char16_t;
using ANSICHAR = char;
using UTF8CHAR = char8_t;

#define TEXT(x) u##x

// ----------------------------------------------------------------------- FString
//
// A UTF-16 string. `operator*` yields the null-terminated buffer, which is the spelling the
// generated code uses when it hands a string to something taking a raw pointer.

class FString
{
public:
    FString() = default;

    FString(const TCHAR* Text) : Storage(Text ? Text : u"") {}

    /// The two-argument form the reader uses: a length and a buffer that is not terminated.
    FString(int32 Length, const TCHAR* Source)
        : Storage(Source, Source + (Length > 0 ? Length : 0))
    {
    }

    int32 Len() const { return static_cast<int32>(Storage.size()); }
    bool IsEmpty() const { return Storage.empty(); }
    void Reset() { Storage.clear(); }

    const TCHAR* operator*() const { return Storage.c_str(); }

    FString operator+(const FString& Other) const
    {
        FString Result;
        Result.Storage = Storage + Other.Storage;
        return Result;
    }

    bool operator==(const FString& Other) const { return Storage == Other.Storage; }

    FString& operator+=(const FString& Other)
    {
        Storage += Other.Storage;
        return *this;
    }

    /// Printf, which the reader uses to build its failure messages. Narrow inside, because the
    /// only thing that reads these back is a test asserting they are non-empty.
    template <typename... TArgs>
    static FString Printf(const TCHAR* Format, TArgs... Args)
    {
        std::string Narrow = Narrowed(Format);

        char Buffer[1024];
        std::snprintf(Buffer, sizeof Buffer, Narrow.c_str(), Args...);

        FString Result;
        for (const char* At = Buffer; *At != '\0'; ++At)
            Result.Storage.push_back(static_cast<TCHAR>(static_cast<unsigned char>(*At)));

        return Result;
    }

    /// The characters as UTF-8, which is what the harness prints.
    std::string ToUtf8() const
    {
        std::string Out;

        for (std::size_t At = 0; At < Storage.size(); ++At)
        {
            char32_t Point = Storage[At];

            // A surrogate pair is one code point written as two units.
            if (Point >= 0xD800 && Point <= 0xDBFF && At + 1 < Storage.size()
                && Storage[At + 1] >= 0xDC00 && Storage[At + 1] <= 0xDFFF)
            {
                Point = 0x10000 + ((Point - 0xD800) << 10) + (Storage[At + 1] - 0xDC00);
                ++At;
            }

            if (Point < 0x80)
            {
                Out.push_back(static_cast<char>(Point));
            }
            else if (Point < 0x800)
            {
                Out.push_back(static_cast<char>(0xC0 | (Point >> 6)));
                Out.push_back(static_cast<char>(0x80 | (Point & 0x3F)));
            }
            else if (Point < 0x10000)
            {
                Out.push_back(static_cast<char>(0xE0 | (Point >> 12)));
                Out.push_back(static_cast<char>(0x80 | ((Point >> 6) & 0x3F)));
                Out.push_back(static_cast<char>(0x80 | (Point & 0x3F)));
            }
            else
            {
                Out.push_back(static_cast<char>(0xF0 | (Point >> 18)));
                Out.push_back(static_cast<char>(0x80 | ((Point >> 12) & 0x3F)));
                Out.push_back(static_cast<char>(0x80 | ((Point >> 6) & 0x3F)));
                Out.push_back(static_cast<char>(0x80 | (Point & 0x3F)));
            }
        }

        return Out;
    }

    /// A path, for handing to the standard library's file streams.
    std::string ToNarrowPath() const { return ToUtf8(); }

private:
    static std::string Narrowed(const TCHAR* Text)
    {
        std::string Out;
        for (; *Text != 0; ++Text)
            Out.push_back(static_cast<char>(*Text));

        return Out;
    }

    std::u16string Storage;
};

/// `TEXT("x") + SomeFString`, which the generated accessor writes when it builds a table's file
/// name. A member operator+ only covers an FString on the left.
inline FString operator+(const TCHAR* Left, const FString& Right)
{
    return FString(Left) + Right;
}

// -------------------------------------------------------------------- FUTF8ToTCHAR
//
// The engine's conversion helper, with the explicit-length constructor the reader uses because
// the bytes in a table file are not null terminated.

class FUTF8ToTCHAR
{
public:
    FUTF8ToTCHAR(const UTF8CHAR* Bytes, int32 Count)
    {
        const auto* At = reinterpret_cast<const unsigned char*>(Bytes);
        const auto* End = At + (Count > 0 ? Count : 0);

        while (At < End)
        {
            char32_t Point = 0;
            int Extra = 0;

            if (*At < 0x80)            { Point = *At;        Extra = 0; }
            else if ((*At & 0xE0) == 0xC0) { Point = *At & 0x1F; Extra = 1; }
            else if ((*At & 0xF0) == 0xE0) { Point = *At & 0x0F; Extra = 2; }
            else                       { Point = *At & 0x07; Extra = 3; }

            ++At;

            for (int Index = 0; Index < Extra && At < End; ++Index, ++At)
                Point = (Point << 6) | (*At & 0x3F);

            if (Point < 0x10000)
            {
                Storage.push_back(static_cast<TCHAR>(Point));
            }
            else
            {
                Point -= 0x10000;
                Storage.push_back(static_cast<TCHAR>(0xD800 + (Point >> 10)));
                Storage.push_back(static_cast<TCHAR>(0xDC00 + (Point & 0x3FF)));
            }
        }
    }

    int32 Length() const { return static_cast<int32>(Storage.size()); }
    const TCHAR* Get() const { return Storage.c_str(); }

private:
    std::u16string Storage;
};

// ------------------------------------------------------------------------ TArray

/// The engine's spelling of std::move. Generated code uses it to hand a whole load over
/// rather than copying it row by row.
template <typename T>
constexpr typename std::remove_reference<T>::type&& MoveTemp(T&& Value)
{
    return static_cast<typename std::remove_reference<T>::type&&>(Value);
}

template <typename T>
class TArray
{
public:
    TArray() = default;

    // The engine's `TArray` takes a brace list, and a generated constant is written as one -
    // an array constant is a list of literals and there is nothing else to write it as.
    TArray(std::initializer_list<T> Values) : Storage(Values) {}

    int32 Num() const { return static_cast<int32>(Storage.size()); }

    T* GetData() { return Storage.data(); }
    const T* GetData() const { return Storage.data(); }

    T& operator[](int32 Index) { return Storage[static_cast<std::size_t>(Index)]; }
    const T& operator[](int32 Index) const { return Storage[static_cast<std::size_t>(Index)]; }

    void Add(const T& Value) { Storage.push_back(Value); }

    bool IsValidIndex(int32 Index) const
    {
        return Index >= 0 && Index < Num();
    }
    void Reserve(int32 Count) { Storage.reserve(static_cast<std::size_t>(Count)); }
    void SetNum(int32 Count) { Storage.resize(static_cast<std::size_t>(Count)); }

    /// Sizes the array and zeroes what it holds, which is how a presence bitmap is read.
    void SetNumZeroed(int32 Count) { Storage.assign(static_cast<std::size_t>(Count), T()); }

    /// Clears without giving the storage back, which is what the engine's Reset does.
    void Reset() { Storage.clear(); }

    /// Empty() clears; Empty(n) clears and reserves, which is how the generated code sizes a
    /// table before filling it.
    void Empty(int32 Slack = 0)
    {
        Storage.clear();
        if (Slack > 0)
            Storage.reserve(static_cast<std::size_t>(Slack));
    }

    /// Appends a default-constructed element and hands back a reference to it.
    T& AddDefaulted_GetRef()
    {
        Storage.emplace_back();
        return Storage.back();
    }

    auto begin() { return Storage.begin(); }
    auto end() { return Storage.end(); }
    auto begin() const { return Storage.begin(); }
    auto end() const { return Storage.end(); }

private:
    std::vector<T> Storage;
};

template <typename T>
class TArrayView
{
public:
    TArrayView() = default;
    TArrayView(T* Pointer, int32 Count) : Pointer(Pointer), Count(Count) {}

    int32 Num() const { return Count; }
    T* GetData() const { return Pointer; }
    T& operator[](int32 Index) const { return Pointer[Index]; }

private:
    T* Pointer = nullptr;
    int32 Count = 0;
};

// -------------------------------------------------------------------------- TMap

template <typename TKey, typename TValue>
class TMap
{
public:
    void Add(const TKey& Key, const TValue& Value) { Storage[Key] = Value; }

    const TValue* Find(const TKey& Key) const
    {
        const auto Found = Storage.find(Key);
        return Found == Storage.end() ? nullptr : &Found->second;
    }

    bool Contains(const TKey& Key) const { return Storage.find(Key) != Storage.end(); }

    void Empty(int32 Slack = 0)
    {
        Storage.clear();
        if (Slack > 0)
            Storage.reserve(static_cast<std::size_t>(Slack));
    }

private:
    std::unordered_map<TKey, TValue> Storage;
};

// -------------------------------------------------------------------------- FGuid
//
// Four uint32s, in the order the engine declares them. The reader assembles them from .NET's
// sixteen-byte layout, and getting that assembly right is the whole of what the corpus checks
// here - so this only has to store them and hand them back.

struct FGuid
{
    FGuid() = default;
    FGuid(uint32 InA, uint32 InB, uint32 InC, uint32 InD) : A(InA), B(InB), C(InC), D(InD) {}

    uint32 A = 0;
    uint32 B = 0;
    uint32 C = 0;
    uint32 D = 0;
};

// ----------------------------------------------------------------- FDateTime, FTimespan
//
// Both count 100-nanosecond ticks, FDateTime from 0001-01-01 and FTimespan as a signed
// duration - the same units .NET uses, which is why the reader converts nothing.

struct FDateTime
{
    FDateTime() = default;
    explicit FDateTime(int64 InTicks) : Ticks(InTicks) {}

    int64 GetTicks() const { return Ticks; }

    /// 9999-12-31 23:59:59.9999999, the same as .NET's DateTime.MaxValue.
    static FDateTime MaxValue() { return FDateTime(3155378975999999999LL); }

    int64 Ticks = 0;
};

struct FTimespan
{
    FTimespan() = default;
    explicit FTimespan(int64 InTicks) : Ticks(InTicks) {}

    int64 GetTicks() const { return Ticks; }

    int64 Ticks = 0;
};

// ------------------------------------------------------------------ FMemory, FMath

struct FMemory
{
    static void* Memcpy(void* Destination, const void* Source, std::size_t Count)
    {
        return std::memcpy(Destination, Source, Count);
    }
};

struct FMath
{
    template <typename T>
    static T Clamp(T Value, T Low, T High)
    {
        return Value < Low ? Low : (Value > High ? High : Value);
    }
};

// ------------------------------------------------------------------------ logging
//
// Onto standard error, which is where a test can read it. It used to be a no-op on the
// grounds that the harness fails on the return value and nothing reads the reason - and then
// a gate arrived that asks *why* a load was refused, and the answer was being thrown away
// here. A generated reader that says "the file does not match its MAC" and a stub that
// discards it are indistinguishable from a reader that says nothing.
//
// No format specifiers are honoured. The format string and each argument are printed in
// order, which is enough for a test asking whether a particular reason came out, and stops
// short of putting a formatter this fixture does not own between the reader and the check.

struct FLogCategoryStub {};

inline FLogCategoryStub LogTemp;

namespace ELogVerbosityStub { enum Type { Error, Warning, Log, Verbose }; }

using namespace ELogVerbosityStub;

inline void LogStubValue(const FString& value) { std::cerr << value.ToUtf8() << ' '; }

inline void LogStubValue(const TCHAR* value) { LogStubValue(FString(value)); }

/// A `TEXT("...")` literal, which is an array rather than a pointer until it decays.
template <std::size_t N>
inline void LogStubValue(const TCHAR (&value)[N]) { LogStubValue(FString(value)); }

template <typename T>
inline void LogStubValue(const T& value) { std::cerr << value << ' '; }

inline void LogStubLine() { std::cerr << std::endl; }

template <typename First, typename... Rest>
inline void LogStubLine(const First& first, const Rest&... rest)
{
    LogStubValue(first);
    LogStubLine(rest...);
}

#define UE_LOG(Category, Verbosity, Format, ...) LogStubLine(Format, ##__VA_ARGS__)

// ------------------------------------------------------------------ UHT macros
//
// No-ops. Unreal Header Tool turns these into reflection data, and none of the generated code's
// behaviour depends on that - the tables are plain structs read by plain functions. What the
// macros do buy at runtime is Blueprint visibility, which is checked by UnrealTargetTests
// reading the generated text rather than by running anything.

#define UMETA(...)
#define USTRUCT(...)
#define UCLASS(...)
#define UENUM(...)
#define UPROPERTY(...)
#define UFUNCTION(...)
#define UINTERFACE(...)
#define GENERATED_BODY(...)
#define GENERATED_USTRUCT_BODY(...)
