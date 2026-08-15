// FPaths::Combine, which the generated accessor uses to build each table's path.
//
// A forward slash on every platform, as the engine's does - Windows accepts it, and it keeps
// the generated behaviour the same wherever the harness runs.

#pragma once

#include "CoreMinimal.h"

struct FPaths
{
    static FString Combine(const FString& Only) { return Only; }

    /// Variadic, as the engine's is.
    template <typename... TRest>
    static FString Combine(const FString& First, const TRest&... Rest)
    {
        const FString Tail = Combine(Rest...);

        if (First.IsEmpty())
        {
            return Tail;
        }

        return First + FString(TEXT("/")) + Tail;
    }
};
