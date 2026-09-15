namespace CoaiMcp.Core.Normalising;

/// <summary>
/// The only words a skeleton may keep: the language's own runtime, and nothing else.
/// </summary>
/// <remarks>
/// <para><b>This list IS the anonymity guarantee</b>, and it is a whitelist rather than a blacklist
/// on purpose. "Remove everything that looks like a domain name" cannot be checked, because the set
/// of domain names is unbounded and unknown. "Keep nothing but these words" can: every identifier in
/// a skeleton is either a generated placeholder or a member of this set, and anything else is a leak
/// by definition. The server applies the same shape at ingest, where it has never seen the original
/// and could not run a blacklist even if it wanted to.</para>
/// <para><b>The rule for what belongs here: it must ship with the language.</b> `ConcurrentDictionary`
/// and `Promise` are facts about a runtime, shared by every program that uses one, and they carry the
/// failure physics the corpus exists to teach — a check-then-act race is `ContainsKey` followed by
/// `Add`, and losing those two words loses the bug. `InvoiceService` is a fact about us. Nothing that
/// is a fact about us goes in this file, and the test for a new entry is whether a stranger reading
/// only the standard library documentation would recognise it.</para>
/// <para>It is deliberately not exhaustive. A runtime name that is missing gets RENAMED, which costs
/// some signal and leaks nothing; a domain name wrongly added leaks everything. The asymmetry decides
/// every judgement call here, and it is why this errs small.</para>
/// </remarks>
public static class RuntimeVocabulary
{
    /// <summary>What a skeleton in <paramref name="language"/> may keep verbatim.</summary>
    public static IReadOnlySet<string> For(SourceLanguage language) => language switch
    {
        SourceLanguage.CSharp => CSharp,
        SourceLanguage.TypeScript or SourceLanguage.JavaScript => Script,
        _ => Nothing,
    };

    private static readonly HashSet<string> Nothing = new(StringComparer.Ordinal);

    /// <remarks>
    /// Grouped by what each group is evidence OF, because that is the question a reader of a
    /// skeleton is asking: was this concurrent, was it asynchronous, did it hold a resource.
    /// </remarks>
    private static readonly HashSet<string> CSharp = new(StringComparer.Ordinal)
    {
        // Synchronisation — the presence or absence of these is the whole story of a race.
        "Monitor", "Interlocked", "SemaphoreSlim", "Semaphore", "Mutex", "SpinLock", "SpinWait",
        "ReaderWriterLockSlim", "Barrier", "CountdownEvent", "ManualResetEvent", "ManualResetEventSlim",
        "AutoResetEvent", "WaitHandle", "Volatile", "MemoryBarrier", "Lazy", "AsyncLocal", "ThreadLocal",

        // Asynchrony, and the places it goes wrong: a missing await, a blocked wait, a dropped token.
        "Task", "ValueTask", "Thread", "ThreadPool", "CancellationToken", "CancellationTokenSource",
        "ConfigureAwait", "GetAwaiter", "GetResult", "Wait", "WaitAsync", "Result", "Delay", "Yield",
        "WhenAll", "WhenAny", "Run", "StartNew", "ContinueWith", "FromResult", "CompletedTask",
        "IsCancellationRequested", "ThrowIfCancellationRequested", "Register", "Cancel", "CancelAsync",

        // Collections, and the member names that make check-then-act visible as a shape.
        "Dictionary", "List", "HashSet", "Queue", "Stack", "Array", "Span", "Memory",
        "ConcurrentDictionary", "ConcurrentQueue", "ConcurrentBag", "ConcurrentStack",
        "BlockingCollection", "ImmutableArray", "ImmutableDictionary", "ImmutableList",
        "ContainsKey", "TryGetValue", "TryAdd", "TryRemove", "TryTake", "TryPop", "TryDequeue",
        "GetOrAdd", "AddOrUpdate", "Add", "Remove", "Contains", "Clear", "Count", "Length",
        "Enqueue", "Dequeue", "Push", "Pop", "ToArray", "ToList", "Any", "First", "FirstOrDefault",

        // LINQ. It ships with the language and it is half of what a method SHAPE looks like in C#;
        // renaming `Select` to `method_2` makes two identical pipelines look like different code.
        "Select", "SelectMany", "Where", "OrderBy", "OrderByDescending", "ThenBy", "GroupBy",
        "Aggregate", "Sum", "Min", "Max", "Average", "Distinct", "Concat", "Union", "Except",
        "Intersect", "Take", "Skip", "TakeWhile", "SkipWhile", "Single", "SingleOrDefault",
        "Last", "LastOrDefault", "All", "Zip", "Reverse", "Cast", "OfType", "ToDictionary",
        "ToHashSet", "ToLookup", "Except", "DefaultIfEmpty", "ElementAt", "Order",

        // Lifetime. A disposal that does not happen is the other half of the resource-leak corpus.
        "IDisposable", "IAsyncDisposable", "Dispose", "DisposeAsync", "Close", "Flush",
        "IEnumerable", "IAsyncEnumerable", "IReadOnlyList", "IReadOnlyDictionary", "ICollection",

        // The primitives themselves, which say what is being shared without saying what it means.
        "string", "int", "long", "bool", "double", "float", "decimal", "byte", "char", "object",
        "void", "var", "dynamic", "nint", "Nullable", "String", "Int32", "Int64", "Boolean", "Object",
        "Exception", "OperationCanceledException", "TaskCanceledException", "TimeSpan", "DateTime",
    };

    /// <remarks>
    /// One set for TypeScript and JavaScript: they share a runtime, and the corpus filters by
    /// language before it compares anything, so a word the other language never uses costs nothing.
    /// </remarks>
    private static readonly HashSet<string> Script = new(StringComparer.Ordinal)
    {
        // Asynchrony — and every way a promise is mishandled.
        "Promise", "then", "catch", "finally", "resolve", "reject", "all", "allSettled", "race",
        "any", "async", "await", "AsyncGenerator", "Generator", "queueMicrotask",
        "setTimeout", "setInterval", "clearTimeout", "clearInterval", "setImmediate",
        "AbortController", "AbortSignal", "abort", "signal", "aborted", "onabort",

        // Collections, and the member names a race shows up in.
        "Map", "Set", "WeakMap", "WeakSet", "Array", "Object", "JSON", "Symbol", "Proxy", "Reflect",
        "has", "get", "set", "delete", "clear", "size", "length", "keys", "values", "entries",
        "push", "pop", "shift", "unshift", "splice", "slice", "concat", "join", "indexOf",
        "forEach", "map", "filter", "reduce", "find", "some", "every", "sort", "includes",
        "parse", "stringify", "freeze", "assign", "create", "defineProperty",

        // Shared memory and workers: the only true parallelism this runtime has.
        "Atomics", "SharedArrayBuffer", "ArrayBuffer", "Worker", "MessageChannel", "postMessage",
        "addEventListener", "removeEventListener", "dispatchEvent",

        // The network and the primitives, which say what is shared without saying what it means.
        "fetch", "Response", "Request", "Headers", "URL", "URLSearchParams", "ReadableStream",
        "Error", "TypeError", "RangeError", "Number", "String", "Boolean", "BigInt", "Date",
        "undefined", "null", "NaN", "Infinity", "globalThis", "console",
    };
}
