// FFileHelper, which is how the generated code reads a table file.
//
// In the engine this goes through IPlatformFile, so the same call reads a loose file in the
// editor and one inside a .pak in a cooked build. Off-engine there is no .pak, so this is an
// ifstream - the path it takes is not what the corpus is checking, the bytes it hands back are.

#pragma once

#include "CoreMinimal.h"

struct FFileHelper
{
    static bool LoadFileToArray(TArray<uint8>& Out, const TCHAR* Filename)
    {
        FString Path(Filename);

        std::ifstream Stream(Path.ToNarrowPath(), std::ios::binary);
        if (!Stream)
        {
            return false;
        }

        Stream.seekg(0, std::ios::end);
        const std::streamoff Length = Stream.tellg();
        Stream.seekg(0, std::ios::beg);

        if (Length < 0)
        {
            return false;
        }

        Out.Empty(static_cast<int32>(Length));
        Out.SetNum(static_cast<int32>(Length));

        if (Length > 0)
        {
            Stream.read(reinterpret_cast<char*>(Out.GetData()), Length);
        }

        return static_cast<bool>(Stream);
    }
};
