﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Legacy;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;

namespace Microsoft.VisualStudio.Editor.Razor
{
    internal class RazorDirectiveCompletionSource : IAsyncCompletionSource
    {
        // Internal for testing
        internal static readonly object DescriptionKey = new object();
        internal static readonly AccessibleImageId DirectiveImageGlyph = new AccessibleImageId(
            new ImageId(KnownImageIds.ImageCatalogGuid, KnownImageIds.Type),
            "Razor Directive.");
        internal static readonly ImmutableArray<CompletionFilter> DirectiveCompletionFilters = new[] {
            new CompletionFilter("Razor Directive", "r", DirectiveImageGlyph)
        }.ToImmutableArray();
        private static readonly IEnumerable<DirectiveDescriptor> DefaultDirectives = new[]
        {
            CSharpCodeParser.AddTagHelperDirectiveDescriptor,
            CSharpCodeParser.RemoveTagHelperDirectiveDescriptor,
            CSharpCodeParser.TagHelperPrefixDirectiveDescriptor,
        };

        // Internal for testing
        internal readonly VisualStudioRazorParser _parser;
        private readonly ForegroundDispatcher _foregroundDispatcher;

        public RazorDirectiveCompletionSource(
            VisualStudioRazorParser parser,
            ForegroundDispatcher foregroundDispatcher)
        {
            if (parser == null)
            {
                throw new ArgumentNullException(nameof(parser));
            }

            if (foregroundDispatcher == null)
            {
                throw new ArgumentNullException(nameof(foregroundDispatcher));
            }

            _parser = parser;
            _foregroundDispatcher = foregroundDispatcher;
        }

        public Task<CompletionContext> GetCompletionContextAsync(
            CompletionTrigger trigger, 
            SnapshotPoint triggerLocation, 
            SnapshotSpan applicableSpan, 
            CancellationToken token)
        {
            _foregroundDispatcher.AssertBackgroundThread();

            var syntaxTree = _parser.CodeDocument?.GetSyntaxTree();
            if (!AtDirectiveCompletionPoint(syntaxTree, triggerLocation))
            {
                return Task.FromResult(new CompletionContext(ImmutableArray<CompletionItem>.Empty));
            }

            var completionItems = GetCompletionItems(syntaxTree);
            var context = new CompletionContext(completionItems.ToImmutableArray());
            return Task.FromResult(context);
        }

        public Task<object> GetDescriptionAsync(CompletionItem item, CancellationToken token)
        {
            if (!item.Properties.TryGetProperty<string>(DescriptionKey, out var directiveDescription))
            {
                directiveDescription = string.Empty;
            }

            return Task.FromResult<object>(directiveDescription);
        }

        public bool TryGetApplicableSpan(char typeChar, SnapshotPoint triggerLocation, out SnapshotSpan applicableSpan)
        {
            // The applicable span for completion is the piece of text a completion is for. For example:
            //      @Date|Time.Now
            // If you trigger completion at the | then the applicable span is the region of 'DateTime'; however, Razor
            // doesn't know this information so we rely on Roslyn to define what the applicable span for a completion is.
            applicableSpan = default(SnapshotSpan);
            return false;
        }

        // Internal for testing
        internal List<CompletionItem> GetCompletionItems(RazorSyntaxTree syntaxTree)
        {
            var directives = syntaxTree.Options.Directives.Concat(DefaultDirectives);
            var completionItems = new List<CompletionItem>();
            foreach (var directive in directives)
            {
                var completionDisplayText = directive.DisplayName ?? directive.Directive;
                var completionItem = new CompletionItem(
                    displayText: completionDisplayText,
                    filterText: completionDisplayText,
                    insertText: directive.Directive,
                    source: this,
                    image: DirectiveImageGlyph,
                    filters: DirectiveCompletionFilters,
                    suffix: string.Empty,
                    sortText: "_RazorDirective_", // This groups all Razor directives together
                    attributeImages: ImmutableArray<AccessibleImageId>.Empty);
                completionItem.Properties.AddProperty(DescriptionKey, directive.Description);
                completionItems.Add(completionItem);
            }

            return completionItems;
        }

        // Internal for testing
        internal static bool AtDirectiveCompletionPoint(RazorSyntaxTree syntaxTree, SnapshotPoint location)
        {
            if (syntaxTree == null)
            {
                return false;
            }

            var change = new SourceChange(location.Position, 0, string.Empty);
            var owner = syntaxTree.Root.LocateOwner(change);

            if (owner == null)
            {
                return false;
            }

            if (owner.ChunkGenerator is ExpressionChunkGenerator &&
                owner.Symbols.All(IsDirectiveCompletableSymbol) &&
                // Do not provide IntelliSense for explicit expressions. Explicit expressions will usually look like:
                // [@] [(] [DateTime.Now] [)]
                owner.Parent?.Children.Count > 1 &&
                owner.Parent.Children[1] == owner)
            {
                return true;
            }

            return false;
        }

        // Internal for testing
        internal static bool IsDirectiveCompletableSymbol(ISymbol symbol)
        {
            if (!(symbol is CSharpSymbol csharpSymbol))
            {
                return false;
            }

            return csharpSymbol.Type == CSharpSymbolType.Identifier ||
                // Marker symbol
                csharpSymbol.Type == CSharpSymbolType.Unknown;
        }
    }
}