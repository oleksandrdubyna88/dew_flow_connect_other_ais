using CoaiMcp.Core.Security;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Pins what <see cref="SecurityLaneSetting.Parse"/> answers for every branch of its validation — the exact
/// complaint texts and their order, which refusal wins when several apply, and the values a refused pairing
/// keeps — so splitting its methods to complexity 4 cannot change any of it silently.
/// </summary>
/// <remarks>
/// Written against the code BEFORE that split and observed green there
/// (todo/PLAN_security_lane_methods_within_complexity_4.md). <see cref="SecurityLaneSettingsTests"/> asserts
/// the lane's guarantees; this file asserts its exact present behaviour, which is what a behaviour-neutral
/// refactor promises to keep.
/// </remarks>
public sealed class SecurityLaneSettingCharacterizationTests
{
    internal static readonly ProviderSettings[] Providers =
    [
        new("qwen") { Runtime = "local", Model = "fixture" },
        new("codex") { Runtime = "codex" },
    ];

    private const string Lane = "\"enabled\":true";

    private static string Prompts(int count, Func<int, string> id) =>
        "[" + string.Join(",", Enumerable.Range(0, count).Select(i => $"{{\"id\":\"{id(i)}\"}}")) + "]";

    private static string Runs(int count) =>
        "[" + string.Join(",", Enumerable.Range(0, count).Select(i => $"{{\"vendor\":\"qwen\",\"prompt\":\"{SecurityCatalog.Prompts[i % 12].Id}\",\"context\":\"{(i < 12 ? "slice" : "diff")}\"}}")) + "]";

    internal static readonly (string Name, string? Json)[] Inputs =
    [
        ("null", null),
        ("blank", "   "),
        ("one byte over 128 KiB", "{\"enabled\":true}" + new string(' ', 131073 - 16)),
        ("exactly 128 KiB", "{\"enabled\":true}" + new string(' ', 131072 - 16)),
        ("128 KiB counted in UTF-8 bytes, not characters", "{\"enabled\":true,\"x\":\"" + new string('\u00e9', 65536) + "\"}"),
        ("invalid JSON", "{"),
        ("deeper than 16", "{\"enabled\":true,\"x\":" + new string('[', 16) + new string(']', 16) + "}"),
        ("root array", "[]"),
        ("root string", "\"lane\""),
        ("preserved malformed setting", "{\"enabled\":false,\"invalidConfiguration\":\"{\"}"),
        ("preserved beside unknown members", "{\"future\":1,\"invalidConfiguration\":\"{\"}"),
        ("duplicate root member", "{\"enabled\":true,\"enabled\":true}"),
        ("two unknown root members", "{\"alpha\":1,\"beta\":2,\"enabled\":true}"),
        ("prompts and runs both objects", "{" + Lane + ",\"prompts\":{},\"runs\":{}}"),
        ("runs an object", "{" + Lane + ",\"runs\":{}}"),
        ("enabled a string", "{\"enabled\":\"yes\"}"),
        ("enabled null", "{\"enabled\":null}"),
        ("threshold above 100", "{" + Lane + ",\"threshold\":101}"),
        ("threshold fractional", "{" + Lane + ",\"threshold\":1.5}"),
        ("threshold a string", "{" + Lane + ",\"threshold\":\"1\"}"),
        ("maxRounds zero", "{" + Lane + ",\"maxRounds\":0}"),
        ("maxRounds eleven with a valid threshold", "{" + Lane + ",\"threshold\":3,\"maxRounds\":11}"),
        ("upper bounds accepted", "{" + Lane + ",\"threshold\":100,\"maxRounds\":10}"),
        ("lower bounds accepted, lane off", "{\"enabled\":false,\"threshold\":0,\"maxRounds\":1}"),
        ("no enabled member", "{\"threshold\":2}"),
        ("prompt slugs refused", "{" + Lane + ",\"prompts\":[{\"id\":\"sql\"},{\"id\":\"redteam-\"},{\"id\":\"redteam-Upper\"},5,{\"id\":\"redteam-x\"},{\"id\":\"redteam-x\"}]}"),
        ("thirty-three prompt entries", "{" + Lane + ",\"prompts\":" + Prompts(33, i => $"redteam-p{i}") + "}"),
        ("twenty-one new prompts overflow the library", "{" + Lane + ",\"prompts\":" + Prompts(21, i => $"redteam-p{i}") + "}"),
        ("preset with no triggers", "{" + Lane + ",\"prompts\":[{\"id\":\"redteam-sql\",\"triggers\":[]}]}"),
        ("preset with an unknown member", "{" + Lane + ",\"prompts\":[{\"id\":\"redteam-sql\",\"future\":1}]}"),
        ("preset with an unknown member and no triggers", "{" + Lane + ",\"prompts\":[{\"id\":\"redteam-sql\",\"future\":1,\"triggers\":[]}]}"),
        ("preset with a duplicate member", "{" + Lane + ",\"prompts\":[{\"id\":\"redteam-sql\",\"focus\":[],\"focus\":[]}]}"),
        ("unknown trigger beside an unknown member", "{" + Lane + ",\"prompts\":[{\"id\":\"redteam-sql\",\"future\":1,\"triggers\":[\"future\"]}]}"),
        ("triggers not an array", "{" + Lane + ",\"prompts\":[{\"id\":\"redteam-sql\",\"triggers\":\"sql\"}]}"),
        ("triggers with a number", "{" + Lane + ",\"prompts\":[{\"id\":\"redteam-sql\",\"triggers\":[1,\"sql\",\"sql\"]}]}"),
        ("a focus-only signal as a trigger", "{" + Lane + ",\"prompts\":[{\"id\":\"redteam-sql\",\"triggers\":[\"entry-point\"]}]}"),
        ("focus not an array", "{" + Lane + ",\"prompts\":[{\"id\":\"redteam-sql\",\"focus\":\"sql\"}]}"),
        ("focus with unknown and duplicate tags", "{" + Lane + ",\"prompts\":[{\"id\":\"redteam-sql\",\"focus\":[\"entry-point\",\"future\",\"entry-point\",2]}]}"),
        ("unknown focus and an unknown trigger", "{" + Lane + ",\"prompts\":[{\"id\":\"redteam-sql\",\"triggers\":[\"future\"],\"focus\":[\"future\"]}]}"),
        ("custom prompt without triggers", "{" + Lane + ",\"prompts\":[{\"id\":\"redteam-custom\"},{\"id\":\"redteam-other\",\"triggers\":[],\"focus\":[\"sql\"]}]}"),
        ("custom prompt with triggers", "{" + Lane + ",\"prompts\":[{\"id\":\"redteam-custom\",\"triggers\":[\"sql\",\"xss\"],\"focus\":[\"xss\"]}]}"),
        ("preset replaced in place", "{" + Lane + ",\"prompts\":[{\"id\":\"redteam-custom\"},{\"id\":\"redteam-authz\",\"triggers\":[\"sql\"]}]}"),
        ("runs naming unknowns", "{" + Lane + ",\"runs\":[{\"vendor\":\"grok\",\"prompt\":\"redteam-sql\"},{\"vendor\":\"qwen\",\"prompt\":\"redteam-none\"},7,{\"prompt\":\"redteam-sql\"}]}"),
        ("run naming a refused prompt", "{" + Lane + ",\"prompts\":[{\"id\":\"bad\"}],\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"bad\"}]}"),
        ("duplicate pair across case", "{" + Lane + ",\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-sql\"},{\"vendor\":\"QWEN\",\"prompt\":\"redteam-sql\"}]}"),
        ("defaults per runtime", "{" + Lane + ",\"runs\":[{\"vendor\":\"QWEN\",\"prompt\":\"redteam-sql\"},{\"vendor\":\"Codex\",\"prompt\":\"redteam-sql\"}]}"),
        ("explicit contexts", "{" + Lane + ",\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-sql\",\"context\":\"diff\"},{\"vendor\":\"codex\",\"prompt\":\"redteam-sql\",\"context\":\"slice\",\"contextTokens\":1024}]}"),
        ("context not a string", "{" + Lane + ",\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-sql\",\"context\":5}]}"),
        ("context unknown", "{" + Lane + ",\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-sql\",\"context\":\"whole\"}]}"),
        ("context unknown and tokens out of range", "{" + Lane + ",\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-sql\",\"context\":\"whole\",\"contextTokens\":5}]}"),
        ("tokens below range", "{" + Lane + ",\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-sql\",\"contextTokens\":1023}]}"),
        ("tokens above range", "{" + Lane + ",\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-sql\",\"contextTokens\":200001}]}"),
        ("tokens fractional", "{" + Lane + ",\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-sql\",\"contextTokens\":1.5}]}"),
        ("tokens a string", "{" + Lane + ",\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-sql\",\"contextTokens\":\"big\"}]}"),
        ("tokens at both bounds", "{" + Lane + ",\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-sql\",\"contextTokens\":200000},{\"vendor\":\"codex\",\"prompt\":\"redteam-sql\",\"contextTokens\":1024}]}"),
        ("stages not an array", "{" + Lane + ",\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-sql\",\"stages\":\"code\"}]}"),
        ("stages with a number", "{" + Lane + ",\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-sql\",\"stages\":[\"code\",1]}]}"),
        ("stages unknown", "{" + Lane + ",\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-sql\",\"stages\":[\"review\",\"code\"]}]}"),
        ("stages repeated", "{" + Lane + ",\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-sql\",\"stages\":[\"feature\",\"feature\"]}]}"),
        ("stages empty", "{" + Lane + ",\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-sql\",\"stages\":[]}]}"),
        ("unknown run member alone", "{" + Lane + ",\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-sql\",\"future\":1}]}"),
        ("duplicate run member", "{" + Lane + ",\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-sql\",\"stages\":[],\"stages\":[]}]}"),
        ("every run refusal at once", "{" + Lane + ",\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-sql\",\"future\":1,\"context\":\"whole\",\"contextTokens\":5,\"stages\":[\"review\"]}]}"),
        ("seventeen runs", "{" + Lane + ",\"runs\":" + Runs(17) + "}"),
        ("prompt and run complaints in order", "{" + Lane + ",\"prompts\":[{\"id\":\"redteam-custom\",\"focus\":[\"future\"]},{\"id\":\"sql\"}],\"runs\":[{\"vendor\":\"qwen\",\"prompt\":\"redteam-custom\",\"stages\":[\"x\"]},{\"vendor\":\"grok\",\"prompt\":\"redteam-sql\"}]}"),
    ];

    private static readonly Dictionary<string, string> Expected = new()
    {
        ["null"] = "enabled=False threshold=0 maxRounds=2\ncatalog order kept",
        ["blank"] = "enabled=False threshold=0 maxRounds=2\ncatalog order kept",
        ["one byte over 128 KiB"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: configuration exceeds 128 KiB; the lane is off\ncatalog order kept",
        ["exactly 128 KiB"] = "enabled=True threshold=0 maxRounds=2\ncatalog order kept",
        ["128 KiB counted in UTF-8 bytes, not characters"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: configuration exceeds 128 KiB; the lane is off\ncatalog order kept",
        ["invalid JSON"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: invalid JSON; the lane is off\ncatalog order kept",
        ["deeper than 16"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: invalid JSON; the lane is off\ncatalog order kept",
        ["root array"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: expected an object\ncatalog order kept",
        ["root string"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: expected an object\ncatalog order kept",
        ["preserved malformed setting"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: the extension found the security lane configuration malformed; the lane is off until coai.securityLane is corrected\ncatalog order kept",
        ["preserved beside unknown members"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: the extension found the security lane configuration malformed; the lane is off until coai.securityLane is corrected\ncatalog order kept",
        ["duplicate root member"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: duplicate JSON members are not allowed\ncatalog order kept",
        ["two unknown root members"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: unknown members alpha, beta; update this server\ncatalog order kept",
        ["prompts and runs both objects"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: prompts must be an array; the lane is off\ncatalog order kept",
        ["runs an object"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: runs must be an array; the lane is off\ncatalog order kept",
        ["enabled a string"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: enabled must be true or false\ncatalog order kept",
        ["enabled null"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: enabled must be true or false\ncatalog order kept",
        ["threshold above 100"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: threshold must be 0..100 and maxRounds 1..10\ncatalog order kept",
        ["threshold fractional"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: threshold must be 0..100 and maxRounds 1..10\ncatalog order kept",
        ["threshold a string"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: threshold must be 0..100 and maxRounds 1..10\ncatalog order kept",
        ["maxRounds zero"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: threshold must be 0..100 and maxRounds 1..10\ncatalog order kept",
        ["maxRounds eleven with a valid threshold"] = "enabled=False threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: threshold must be 0..100 and maxRounds 1..10\ncatalog order kept",
        ["upper bounds accepted"] = "enabled=True threshold=100 maxRounds=10\ncatalog order kept",
        ["lower bounds accepted, lane off"] = "enabled=False threshold=0 maxRounds=1\ncatalog order kept",
        ["no enabled member"] = "enabled=False threshold=2 maxRounds=2\ncatalog order kept",
        ["prompt slugs refused"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: prompt 'sql' must have a unique redteam- slug\ncomplaint COAI_SECURITY_LANE: prompt 'redteam-' must have a unique redteam- slug\ncomplaint COAI_SECURITY_LANE: prompt 'redteam-Upper' must have a unique redteam- slug\ncomplaint COAI_SECURITY_LANE: prompt '' must have a unique redteam- slug\ncomplaint COAI_SECURITY_LANE: prompt 'redteam-x' must have a unique redteam- slug\ncatalog order kept\nprompt redteam-x triggers=[] focus=[] refusal=",
        ["thirty-three prompt entries"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: prompts is limited to 32; the tail was dropped\ncomplaint COAI_SECURITY_LANE: prompt library is limited to 32\ncomplaint COAI_SECURITY_LANE: prompt library is limited to 32\ncomplaint COAI_SECURITY_LANE: prompt library is limited to 32\ncomplaint COAI_SECURITY_LANE: prompt library is limited to 32\ncomplaint COAI_SECURITY_LANE: prompt library is limited to 32\ncomplaint COAI_SECURITY_LANE: prompt library is limited to 32\ncomplaint COAI_SECURITY_LANE: prompt library is limited to 32\ncomplaint COAI_SECURITY_LANE: prompt library is limited to 32\ncomplaint COAI_SECURITY_LANE: prompt library is limited to 32\ncomplaint COAI_SECURITY_LANE: prompt library is limited to 32\ncomplaint COAI_SECURITY_LANE: prompt library is limited to 32\ncomplaint COAI_SECURITY_LANE: prompt library is limited to 32\ncatalog order kept\nprompt redteam-p0 triggers=[] focus=[] refusal=\nprompt redteam-p1 triggers=[] focus=[] refusal=\nprompt redteam-p2 triggers=[] focus=[] refusal=\nprompt redteam-p3 triggers=[] focus=[] refusal=\nprompt redteam-p4 triggers=[] focus=[] refusal=\nprompt redteam-p5 triggers=[] focus=[] refusal=\nprompt redteam-p6 triggers=[] focus=[] refusal=\nprompt redteam-p7 triggers=[] focus=[] refusal=\nprompt redteam-p8 triggers=[] focus=[] refusal=\nprompt redteam-p9 triggers=[] focus=[] refusal=\nprompt redteam-p10 triggers=[] focus=[] refusal=\nprompt redteam-p11 triggers=[] focus=[] refusal=\nprompt redteam-p12 triggers=[] focus=[] refusal=\nprompt redteam-p13 triggers=[] focus=[] refusal=\nprompt redteam-p14 triggers=[] focus=[] refusal=\nprompt redteam-p15 triggers=[] focus=[] refusal=\nprompt redteam-p16 triggers=[] focus=[] refusal=\nprompt redteam-p17 triggers=[] focus=[] refusal=\nprompt redteam-p18 triggers=[] focus=[] refusal=\nprompt redteam-p19 triggers=[] focus=[] refusal=",
        ["twenty-one new prompts overflow the library"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: prompt library is limited to 32\ncatalog order kept\nprompt redteam-p0 triggers=[] focus=[] refusal=\nprompt redteam-p1 triggers=[] focus=[] refusal=\nprompt redteam-p2 triggers=[] focus=[] refusal=\nprompt redteam-p3 triggers=[] focus=[] refusal=\nprompt redteam-p4 triggers=[] focus=[] refusal=\nprompt redteam-p5 triggers=[] focus=[] refusal=\nprompt redteam-p6 triggers=[] focus=[] refusal=\nprompt redteam-p7 triggers=[] focus=[] refusal=\nprompt redteam-p8 triggers=[] focus=[] refusal=\nprompt redteam-p9 triggers=[] focus=[] refusal=\nprompt redteam-p10 triggers=[] focus=[] refusal=\nprompt redteam-p11 triggers=[] focus=[] refusal=\nprompt redteam-p12 triggers=[] focus=[] refusal=\nprompt redteam-p13 triggers=[] focus=[] refusal=\nprompt redteam-p14 triggers=[] focus=[] refusal=\nprompt redteam-p15 triggers=[] focus=[] refusal=\nprompt redteam-p16 triggers=[] focus=[] refusal=\nprompt redteam-p17 triggers=[] focus=[] refusal=\nprompt redteam-p18 triggers=[] focus=[] refusal=\nprompt redteam-p19 triggers=[] focus=[] refusal=",
        ["preset with no triggers"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: redteam-sql: a preset requires at least one trigger; select a condition before enabling it\ncatalog order kept\nprompt redteam-sql triggers=[] focus=[sql,entry-point] refusal=a preset requires at least one trigger; select a condition before enabling it",
        ["preset with an unknown member"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: redteam-sql: unknown members future; update this server\ncatalog order kept\nprompt redteam-sql triggers=[sql] focus=[sql,entry-point] refusal=unknown members future; update this server",
        ["preset with an unknown member and no triggers"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: redteam-sql: a preset requires at least one trigger; select a condition before enabling it\ncatalog order kept\nprompt redteam-sql triggers=[] focus=[sql,entry-point] refusal=a preset requires at least one trigger; select a condition before enabling it",
        ["preset with a duplicate member"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: redteam-sql: duplicate JSON members are not allowed\ncatalog order kept\nprompt redteam-sql triggers=[sql] focus=[] refusal=duplicate JSON members are not allowed",
        ["unknown trigger beside an unknown member"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: redteam-sql: unknown or invalid trigger; update this server or correct the trigger\ncatalog order kept\nprompt redteam-sql triggers=[future] focus=[sql,entry-point] refusal=unknown or invalid trigger; update this server or correct the trigger",
        ["triggers not an array"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: redteam-sql: unknown or invalid trigger; update this server or correct the trigger\ncatalog order kept\nprompt redteam-sql triggers=[] focus=[sql,entry-point] refusal=unknown or invalid trigger; update this server or correct the trigger",
        ["triggers with a number"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: redteam-sql: unknown or invalid trigger; update this server or correct the trigger\ncatalog order kept\nprompt redteam-sql triggers=[sql] focus=[sql,entry-point] refusal=unknown or invalid trigger; update this server or correct the trigger",
        ["a focus-only signal as a trigger"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: redteam-sql: unknown or invalid trigger; update this server or correct the trigger\ncatalog order kept\nprompt redteam-sql triggers=[entry-point] focus=[sql,entry-point] refusal=unknown or invalid trigger; update this server or correct the trigger",
        ["focus not an array"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: redteam-sql: unknown focus tags ignored\ncatalog order kept\nprompt redteam-sql triggers=[sql] focus=[] refusal=",
        ["focus with unknown and duplicate tags"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: redteam-sql: unknown focus tags ignored\ncatalog order kept\nprompt redteam-sql triggers=[sql] focus=[entry-point] refusal=",
        ["unknown focus and an unknown trigger"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: redteam-sql: unknown focus tags ignored\ncomplaint COAI_SECURITY_LANE: redteam-sql: unknown or invalid trigger; update this server or correct the trigger\ncatalog order kept\nprompt redteam-sql triggers=[future] focus=[] refusal=unknown or invalid trigger; update this server or correct the trigger",
        ["custom prompt without triggers"] = "enabled=True threshold=0 maxRounds=2\ncatalog order kept\nprompt redteam-custom triggers=[] focus=[] refusal=\nprompt redteam-other triggers=[] focus=[sql] refusal=",
        ["custom prompt with triggers"] = "enabled=True threshold=0 maxRounds=2\ncatalog order kept\nprompt redteam-custom triggers=[sql,xss] focus=[xss] refusal=",
        ["preset replaced in place"] = "enabled=True threshold=0 maxRounds=2\ncatalog order kept\nprompt redteam-authz triggers=[sql] focus=[authz,entry-point,sql] refusal=\nprompt redteam-custom triggers=[] focus=[] refusal=",
        ["runs naming unknowns"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: pair 'grok/redteam-sql' names an unknown reviewer or prompt and was dropped\ncomplaint COAI_SECURITY_LANE: pair 'qwen/redteam-none' names an unknown reviewer or prompt and was dropped\ncomplaint COAI_SECURITY_LANE: pair '/' names an unknown reviewer or prompt and was dropped\ncomplaint COAI_SECURITY_LANE: pair '/redteam-sql' names an unknown reviewer or prompt and was dropped\ncatalog order kept",
        ["run naming a refused prompt"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: prompt 'bad' must have a unique redteam- slug\ncomplaint COAI_SECURITY_LANE: pair 'qwen/bad' names an unknown reviewer or prompt and was dropped\ncatalog order kept",
        ["duplicate pair across case"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: duplicate pair 'QWEN/redteam-sql' dropped; use different prompts for different runs\ncatalog order kept\nrun qwen/redteam-sql slice 24000 [code,feature] refusal=",
        ["defaults per runtime"] = "enabled=True threshold=0 maxRounds=2\ncatalog order kept\nrun qwen/redteam-sql slice 24000 [code,feature] refusal=\nrun codex/redteam-sql diff 200000 [code,feature] refusal=",
        ["explicit contexts"] = "enabled=True threshold=0 maxRounds=2\ncatalog order kept\nrun qwen/redteam-sql diff 200000 [code,feature] refusal=\nrun codex/redteam-sql slice 1024 [code,feature] refusal=",
        ["context not a string"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: qwen/redteam-sql: context must be slice or diff\ncatalog order kept\nrun qwen/redteam-sql  200000 [code,feature] refusal=context must be slice or diff",
        ["context unknown"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: qwen/redteam-sql: context must be slice or diff\ncatalog order kept\nrun qwen/redteam-sql whole 200000 [code,feature] refusal=context must be slice or diff",
        ["context unknown and tokens out of range"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: qwen/redteam-sql: contextTokens must be 1024..200000\ncatalog order kept\nrun qwen/redteam-sql whole 5 [code,feature] refusal=contextTokens must be 1024..200000",
        ["tokens below range"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: qwen/redteam-sql: contextTokens must be 1024..200000\ncatalog order kept\nrun qwen/redteam-sql slice 1023 [code,feature] refusal=contextTokens must be 1024..200000",
        ["tokens above range"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: qwen/redteam-sql: contextTokens must be 1024..200000\ncatalog order kept\nrun qwen/redteam-sql slice 200001 [code,feature] refusal=contextTokens must be 1024..200000",
        ["tokens fractional"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: qwen/redteam-sql: contextTokens must be 1024..200000\ncatalog order kept\nrun qwen/redteam-sql slice 0 [code,feature] refusal=contextTokens must be 1024..200000",
        ["tokens a string"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: qwen/redteam-sql: contextTokens must be 1024..200000\ncatalog order kept\nrun qwen/redteam-sql slice 24000 [code,feature] refusal=contextTokens must be 1024..200000",
        ["tokens at both bounds"] = "enabled=True threshold=0 maxRounds=2\ncatalog order kept\nrun qwen/redteam-sql slice 200000 [code,feature] refusal=\nrun codex/redteam-sql diff 1024 [code,feature] refusal=",
        ["stages not an array"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: qwen/redteam-sql: stages must contain only code and feature\ncatalog order kept\nrun qwen/redteam-sql slice 24000 [] refusal=stages must contain only code and feature",
        ["stages with a number"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: qwen/redteam-sql: stages must contain only code and feature\ncatalog order kept\nrun qwen/redteam-sql slice 24000 [code] refusal=stages must contain only code and feature",
        ["stages unknown"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: qwen/redteam-sql: stages must contain only code and feature\ncatalog order kept\nrun qwen/redteam-sql slice 24000 [review,code] refusal=stages must contain only code and feature",
        ["stages repeated"] = "enabled=True threshold=0 maxRounds=2\ncatalog order kept\nrun qwen/redteam-sql slice 24000 [feature] refusal=",
        ["stages empty"] = "enabled=True threshold=0 maxRounds=2\ncatalog order kept\nrun qwen/redteam-sql slice 24000 [] refusal=",
        ["unknown run member alone"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: qwen/redteam-sql: unknown members future; update this server\ncatalog order kept\nrun qwen/redteam-sql slice 24000 [code,feature] refusal=unknown members future; update this server",
        ["duplicate run member"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: qwen/redteam-sql: duplicate JSON members are not allowed\ncatalog order kept\nrun qwen/redteam-sql slice 24000 [] refusal=duplicate JSON members are not allowed",
        ["every run refusal at once"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: qwen/redteam-sql: stages must contain only code and feature\ncatalog order kept\nrun qwen/redteam-sql whole 5 [review] refusal=stages must contain only code and feature",
        ["seventeen runs"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: runs is limited to 16; the tail was dropped\ncomplaint COAI_SECURITY_LANE: duplicate pair 'qwen/redteam-authz' dropped; use different prompts for different runs\ncomplaint COAI_SECURITY_LANE: duplicate pair 'qwen/redteam-sql' dropped; use different prompts for different runs\ncomplaint COAI_SECURITY_LANE: duplicate pair 'qwen/redteam-concurrency' dropped; use different prompts for different runs\ncomplaint COAI_SECURITY_LANE: duplicate pair 'qwen/redteam-auth-tokens' dropped; use different prompts for different runs\ncatalog order kept\nrun qwen/redteam-authz slice 24000 [code,feature] refusal=\nrun qwen/redteam-sql slice 24000 [code,feature] refusal=\nrun qwen/redteam-concurrency slice 24000 [code,feature] refusal=\nrun qwen/redteam-auth-tokens slice 24000 [code,feature] refusal=\nrun qwen/redteam-ssrf slice 24000 [code,feature] refusal=\nrun qwen/redteam-webhooks slice 24000 [code,feature] refusal=\nrun qwen/redteam-files slice 24000 [code,feature] refusal=\nrun qwen/redteam-command slice 24000 [code,feature] refusal=\nrun qwen/redteam-deserialize slice 24000 [code,feature] refusal=\nrun qwen/redteam-secrets slice 24000 [code,feature] refusal=\nrun qwen/redteam-prompt-injection slice 24000 [code,feature] refusal=\nrun qwen/redteam-xss slice 24000 [code,feature] refusal=",
        ["prompt and run complaints in order"] = "enabled=True threshold=0 maxRounds=2\ncomplaint COAI_SECURITY_LANE: redteam-custom: unknown focus tags ignored\ncomplaint COAI_SECURITY_LANE: prompt 'sql' must have a unique redteam- slug\ncomplaint COAI_SECURITY_LANE: qwen/redteam-custom: stages must contain only code and feature\ncomplaint COAI_SECURITY_LANE: pair 'grok/redteam-sql' names an unknown reviewer or prompt and was dropped\ncatalog order kept\nprompt redteam-custom triggers=[] focus=[] refusal=\nrun qwen/redteam-custom slice 24000 [x] refusal=stages must contain only code and feature",
    };

    public static TheoryData<string> Names => [.. Inputs.Select(i => i.Name)];

    [Theory]
    [MemberData(nameof(Names))]
    public void Parsing_answers_exactly_what_it_answered_before_the_split(string name)
    {
        var json = Inputs.Single(i => i.Name == name).Json;
        Render(SecurityLaneSetting.Parse(json, Providers)).Should().Be(Expected[name]);
    }

    [Fact]
    public void Every_input_has_an_expectation_and_every_expectation_an_input() =>
        Expected.Keys.Should().BeEquivalentTo(Inputs.Select(i => i.Name));

    internal static string Render(SecurityLaneSetting lane)
    {
        var lines = new List<string> { $"enabled={lane.Enabled} threshold={lane.Threshold} maxRounds={lane.MaxRounds}" };
        lines.AddRange(lane.Complaints.Select(c => "complaint " + c));
        var catalog = SecurityCatalog.Prompts.Select(p => p.Id).ToArray();
        lines.Add(lane.Prompts.Take(catalog.Length).Select(p => p.Id).SequenceEqual(catalog) ? "catalog order kept" : "catalog order CHANGED");
        lines.AddRange(lane.Prompts.Where(Changed).Select(p =>
            $"prompt {p.Id} triggers=[{string.Join(",", p.Triggers)}] focus=[{string.Join(",", p.Focus)}] refusal={p.Refusal}"));
        lines.AddRange(lane.Runs.Select(r =>
            $"run {r.Vendor}/{r.Prompt} {r.Context} {r.ContextTokens} [{string.Join(",", r.Stages)}] refusal={r.Refusal}"));
        return string.Join("\n", lines);
    }

    private static bool Changed(SecurityPrompt prompt)
    {
        var seed = SecurityCatalog.Prompts.FirstOrDefault(p => p.Id == prompt.Id);
        return seed is null || prompt.Refusal.Length > 0
            || !seed.Triggers.SequenceEqual(prompt.Triggers) || !seed.Focus.SequenceEqual(prompt.Focus);
    }
}
