namespace Doka.EntityFrameworkCore.NestedSet.Benchmarks;

/// <summary>Names actual finite chain, fan-out, routing, and rejection datasets.</summary>
public enum BenchmarkChainProfile
{
    /// <summary>Requests two successful hops with one candidate per level.</summary>
    Hops2Fanout1,

    /// <summary>Requests four successful hops with one candidate per level.</summary>
    Hops4Fanout1,

    /// <summary>Requests eight successful hops with one candidate per level.</summary>
    Hops8Fanout1,

    /// <summary>Requests two hops with two candidate paths at each level.</summary>
    Hops2Fanout2,

    /// <summary>Requests four hops with two candidate paths at each level.</summary>
    Hops4Fanout2,

    /// <summary>Requests eight hops with two candidate paths at each level.</summary>
    Hops8Fanout2,

    /// <summary>Requests two hops with four candidate paths at each level.</summary>
    Hops2Fanout4,

    /// <summary>Requests four hops with four candidate paths at each level.</summary>
    Hops4Fanout4,

    /// <summary>Requests eight hops with four candidates; finite frontier overflow remains explicit.</summary>
    Hops8Fanout4,

    /// <summary>Stops on the second exact type group because it has no permitted manager.</summary>
    Missing,

    /// <summary>Rejects two distinct managers rather than choosing a technical identity.</summary>
    Ambiguous,

    /// <summary>Detects the initial person again on a strictly higher group.</summary>
    Cycle,

    /// <summary>Exceeds a one-state frontier before copying additional logical paths.</summary>
    FrontierLimit,

    /// <summary>Distinguishes retained initial routing from the current manager's different routing value.</summary>
    RoutingSwitch,

    /// <summary>Gives the first manager an additional visible own membership for explicit re-expansion.</summary>
    ReExpansion,
}
