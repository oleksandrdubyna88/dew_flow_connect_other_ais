using CoaiMcp.Core.Normalising;
using CoaiMcp.Normalizer;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A method rewritten so that nothing of this project is left in it.
/// </summary>
/// <remarks>
/// <para>The corpus is keyed on the SHAPE of a defect, so the rewriting has one hard requirement and
/// one soft one. Hard: nothing we named may survive, because the entries leave this machine. Soft:
/// what carries the failure physics must survive, or the corpus is a set of identical skeletons that
/// match everything and mean nothing.</para>
/// <para>The two pull against each other, and every judgement in <see cref="RuntimeVocabulary"/> is
/// where the line was drawn.</para>
/// </remarks>
public sealed class SkeletonTests
{
    private readonly IAstNormalizer _normalizer = new TreeSitterNormalizer();

    private const string Racy = """
        public int GetOrAdd(string invoiceKey, int amountDue)
        {
            // The check and the add are not one operation.
            if (!_invoiceTotals.ContainsKey(invoiceKey))
            {
                _invoiceTotals.Add(invoiceKey, amountDue);
                _auditLog.Write("added invoice " + invoiceKey, 42);
            }

            return _invoiceTotals[invoiceKey];
        }
        """;

    /// <summary>A lambda parameter is a name we chose, and it is renamed like any other.</summary>
    /// <remarks>
    /// Found by the property test over this repository's own `AgentLog.cs`, where `f` and `e` survived
    /// into a skeleton while every other name in the same method was renamed. A one-letter name looks
    /// harmless, which is exactly why it needs a test: the rule is that NOTHING of ours survives, and a
    /// rule with a silent exception is not one.
    /// </remarks>
    [Fact]
    public void ALambdaParameterIsRenamedToo()
    {
        const string source = """
            public void Send()
            {
                var names = _invoices.Select(f => f.CustomerName).Where(n => n.Length > 0).ToList();
            }
            """;

        var skeleton = _normalizer.Normalise(SourceLanguage.CSharp, source);

        skeleton.Should().NotMatchRegex(@"f", "a lambda parameter is a name we chose");
        skeleton.Should().NotMatchRegex(@"n");
        skeleton.Should().NotContain("CustomerName").And.NotContain("_invoices");
        skeleton.Should().Contain("Select").And.Contain("Where").And.Contain("ToList");
    }

    [Fact]
    public void NothingWeNamedSurvives()
    {
        var skeleton = _normalizer.Normalise(SourceLanguage.CSharp, Racy);

        foreach (var ours in (string[])["GetOrAdd", "invoiceKey", "amountDue", "_invoiceTotals", "_auditLog", "Write"])
        {
            skeleton.Should().NotContain(ours, $"'{ours}' is a name we chose, and it leaves this machine");
        }
    }

    [Fact]
    public void WhatCarriesTheFailureSurvives()
    {
        var skeleton = _normalizer.Normalise(SourceLanguage.CSharp, Racy);

        skeleton.Should().Contain("ContainsKey", "the check half of check-then-act");
        skeleton.Should().Contain("Add", "and the act half — losing either loses the bug");
        skeleton.Should().Contain("if", "the control flow between them");
        skeleton.Should().Contain("return");
    }

    [Fact]
    public void LiteralsAndCommentsAreGone()
    {
        var skeleton = _normalizer.Normalise(SourceLanguage.CSharp, Racy);

        skeleton.Should().NotContain("added invoice", "a string literal is where a domain leaks in prose");
        skeleton.Should().NotContain("42", "and a constant is where one leaks as a number");
        skeleton.Should().NotContain("The check and the add", "comments say the most of all");
        skeleton.Should().NotContain("//");
    }

    /// <summary>
    /// Two methods that differ only in their names normalise to exactly the same text.
    /// </summary>
    /// <remarks>
    /// This is the property the whole corpus rests on. A vector keyed on shape has to be blind to
    /// renaming, or the same defect in two repositories is two unrelated entries and the gate never
    /// matches anything.
    /// </remarks>
    [Fact]
    public void TwoMethodsDifferingOnlyInNames_NormaliseIdentically()
    {
        const string other = """
            public int FetchOrCreate(string customerRef, int balance)
            {
                // Entirely different words, entirely the same defect.
                if (!_balances.ContainsKey(customerRef))
                {
                    _balances.Add(customerRef, balance);
                    _tracer.Write("created " + customerRef, 7);
                }

                return _balances[customerRef];
            }
            """;

        _normalizer.Normalise(SourceLanguage.CSharp, Racy)
            .Should().Be(_normalizer.Normalise(SourceLanguage.CSharp, other));
    }

    [Fact]
    public void TheSameMethodTwice_NormalisesToTheSameText()
    {
        _normalizer.Normalise(SourceLanguage.CSharp, Racy)
            .Should().Be(_normalizer.Normalise(SourceLanguage.CSharp, Racy), "a skeleton is a key, and a key must not wobble");
    }

    [Fact]
    public void PlaceholdersAreNumberedInOrderOfFirstAppearance()
    {
        var skeleton = _normalizer.Normalise(SourceLanguage.CSharp, Racy);

        skeleton.Should().Contain("method_1", "the method's own name is the first thing renamed");
        skeleton.Should().MatchRegex(@"var_1\b");
    }

    [Fact]
    public void ATypeScriptMethodIsRewrittenToo()
    {
        const string source = """
            async function loadInvoices(customerId: string): Promise<Invoice[]> {
              const rows = await fetch("/api/invoices/" + customerId).then(r => r.json());
              return rows.filter(row => row.total > 100);
            }
            """;

        var skeleton = _normalizer.Normalise(SourceLanguage.TypeScript, source);

        skeleton.Should().NotContain("loadInvoices").And.NotContain("customerId").And.NotContain("Invoice");
        skeleton.Should().NotContain("/api/invoices/").And.NotContain("100");
        skeleton.Should().Contain("await").And.Contain("fetch").And.Contain("then").And.Contain("filter");
    }

    /// <summary>A TYPE is not a string literal, however the grammar spells it.</summary>
    /// <remarks>
    /// tree-sitter names an anonymous token by its own text, so the TypeScript type keyword `string`
    /// is a node whose KIND is "string" — and a literal check written as `kind.Contains("string")`
    /// rewrote it as `""`. `Promise<string[]>` normalised to `Promise<""[]>`, losing exactly the
    /// runtime type the skeleton exists to keep. Found by running the published binary.
    /// </remarks>
    [Fact]
    public void ATypeKeywordSurvives_EvenWhenTheGrammarNamesItAfterAString()
    {
        const string source = """
            async function load(customerId: string): Promise<string[]> {
              return [customerId];
            }
            """;

        var skeleton = _normalizer.Normalise(SourceLanguage.TypeScript, source);

        skeleton.Should().Contain("string", "the TYPE is runtime information the corpus needs");
        skeleton.Should().NotContain("customerId").And.NotContain("load");
    }

    /// <summary>A JavaScript parameter is renamed even when it is spelled like a runtime member.</summary>
    /// <remarks>
    /// JavaScript hangs parameter identifiers straight off `formal_parameters` with no field, so they
    /// looked like references rather than declarations — and `function tally(entries)` kept `entries`,
    /// because `entries` is also `Object.entries`. A parameter named after a runtime member is
    /// precisely what the declaration check exists for. Found by running the published binary.
    /// </remarks>
    [Fact]
    public void AJavaScriptParameterNamedAfterARuntimeMember_IsStillRenamed()
    {
        const string source = """
            function tally(entries, size) {
              return entries.reduce((sum, entry) => sum + entry.amountDue, 0);
            }
            """;

        var skeleton = _normalizer.Normalise(SourceLanguage.JavaScript, source);

        skeleton.Should().NotMatchRegex(@"entries", "it is a parameter WE named, not Object.entries");
        skeleton.Should().NotMatchRegex(@"size");
        skeleton.Should().NotContain("amountDue").And.NotContain("tally");
        skeleton.Should().Contain("reduce", "and the runtime member it calls is still runtime");
    }

    /// <summary>
    /// The whitelist check passes on a real skeleton, and names the leak when one is planted.
    /// </summary>
    /// <remarks>
    /// The second half is what makes the first worth anything: a checker that never fires is
    /// indistinguishable from a checker that cannot.
    /// </remarks>
    [Fact]
    public void TheAlphabetCheck_PassesACleanSkeleton_AndNamesAPlantedLeak()
    {
        var keywords = _normalizer.KeywordsOf(SourceLanguage.CSharp);
        var skeleton = _normalizer.Normalise(SourceLanguage.CSharp, Racy);

        Skeleton.Leaks(skeleton, SourceLanguage.CSharp, keywords)
            .Should().BeEmpty("every word is a placeholder, a keyword, or the runtime's own");

        Skeleton.Leaks(skeleton + " InvoiceService", SourceLanguage.CSharp, keywords)
            .Should().ContainSingle().Which.Should().Be("InvoiceService");
    }
}
