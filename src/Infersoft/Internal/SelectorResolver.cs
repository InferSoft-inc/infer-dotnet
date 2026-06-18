using System;
using System.Collections.Generic;

namespace Infersoft.Internal;

/// <summary>
/// Resolves a resource method's selector input: a ready <see cref="Selectors"/>, or — as a
/// shortcut — a list of document ids wrapped into a file selector. At most one of the two; when
/// the call requires a target, exactly one must be given.
/// </summary>
internal static class SelectorResolver
{
    public static Selectors? Resolve(Selectors? selectors, IEnumerable<long>? documentIds, bool required)
    {
        if (selectors is not null && documentIds is not null)
        {
            throw new ArgumentException("pass either selectors or documentIds, not both", nameof(documentIds));
        }

        if (documentIds is not null)
        {
            return Selectors.Build(include: new[] { Selector.File(documentIds) });
        }

        if (required && selectors is null)
        {
            throw new ArgumentException("pass selectors or documentIds", nameof(selectors));
        }

        return selectors;
    }
}
