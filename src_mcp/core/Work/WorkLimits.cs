namespace CoaiMcp.Core.Work;

/// <summary>
/// The numbers the validator checks a plan against. They are NEVER typed into the validator: the caller passes the device's work method
/// (the <c>roles</c> answer), so a plan valid on one device can be invalid on another and the answer says which numbers it used.
/// </summary>
public sealed record WorkLimits(int MaxEpicsPerEpoch, int MvpMin, int MvpMax, int MaxStoriesPerEpic, int MaxTasksPerStory);
