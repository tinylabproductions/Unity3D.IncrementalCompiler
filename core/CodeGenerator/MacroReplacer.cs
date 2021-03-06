﻿using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace System.Diagnostics.CodeAnalysis {
  [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue, AllowMultiple = true, Inherited = false)]
  sealed class NotNullIfNotNullAttribute : Attribute
  {
    public NotNullIfNotNullAttribute(string parameterName) => ParameterName = parameterName;
    public string ParameterName { get; }
  }
}

namespace IncrementalCompiler {
  public class MacroReplacer : CSharpSyntaxRewriter {
    readonly MacroProcessor.MacroCtx ctx;
    public readonly List<SyntaxNode> successfulEdits = new List<SyntaxNode>();
    readonly List<SyntaxNode> toAdd = new List<SyntaxNode>();

    public MacroReplacer(MacroProcessor.MacroCtx ctx) {
      this.ctx = ctx;
    }

    public void Reset() {
      toAdd.Clear();
    }

    [return: NotNullIfNotNull("node")]
    public override SyntaxNode? Visit(SyntaxNode? node) {
      var rewritten = node;
      if (node != null) {
        if (ctx.AddedStatements.TryGetValue(node, out var added)) toAdd.Add(added);
        if (ctx.ChangedNodes.TryGetValue(node, out var replacement)) {
          rewritten = replacement;
          successfulEdits.Add(node);
          base.Visit(node);
        }
        else {
          rewritten = base.Visit(node)
            .WithLeadingTrivia(node.GetLeadingTrivia())
            .WithTrailingTrivia(node.GetTrailingTrivia());
        }
      }

      return rewritten;
    }

    // used with block statements, type members
    public override SyntaxList<TNode> VisitList<TNode>(SyntaxList<TNode> list) {
      List<TNode>? alternate = null;

      for (int i = 0, n = list.Count; i < n; i++) {
        var item = list[i];

        var visited = VisitListElement(item);
        var replaced = ctx.ChangedStatements.TryGetValue(item, out var replacementList);

        if ((item != visited || replaced) && alternate == null) {
          alternate = new List<TNode>(n);
          // not optimal
          alternate.AddRange(list.Take(i));
        }

        if (replaced) {
          successfulEdits.Add(item);
          for (var index = 0; index < replacementList.Length; index++) {
            // TODO: finish whitespace logic
            var replacement = replacementList[index]?.NormalizeWhitespace();
            if (replacement == null) continue;
            if (index == 0)
              replacement = replacement.WithLeadingTrivia(item.GetLeadingTrivia());
            if (index == replacementList.Length - 1)
              replacement = replacement.WithTrailingTrivia(item.GetTrailingTrivia());
            else
              replacement = replacement.WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.LineFeed));
            alternate!.Add((TNode) replacement);
          }
        }

        if (alternate != null && visited != null && !visited.IsKind(SyntaxKind.None) && !replaced)
          alternate.Add(visited);
      }

      if (toAdd.Count > 0)
        if (typeof(TNode) == typeof(MemberDeclarationSyntax)
            || typeof(TNode) == typeof(StatementSyntax)) {
          if (alternate == null) alternate = new List<TNode>(list);
          foreach (var sn in toAdd) alternate.Add((TNode) sn);
          toAdd.Clear();
        }

      return alternate != null ? SyntaxFactory.List(alternate) : list;
    }
  }
}
