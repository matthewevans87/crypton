using Microsoft.Extensions.AI;

namespace Crypton.Tools;

/// <summary>
/// An <see cref="ITool"/> that additionally exposes itself as a <see cref="AIFunction"/>
/// so it can be surfaced in an AI agent's tool list.
/// </summary>
public interface IAgentTool : ITool
{
    AIFunction AsAIFunction();
}
