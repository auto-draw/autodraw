using System.Threading;
using System.Threading.Tasks;
using Autodraw.Configuration;

namespace Autodraw.Painting.Recreate;

/// <summary>
/// Small surface the recreate engine needs.  It is deliberately independent of Win32 so the
/// engine, picker, and tests can run without touching the physical mouse.
/// </summary>
public interface IInputSimulator
{
    Task MoveToAsync(ScreenPoint point, CancellationToken cancellationToken);
    Task LeftDownAsync(CancellationToken cancellationToken);
    Task LeftUpAsync(CancellationToken cancellationToken);
    Task ClickAsync(ScreenPoint point, CancellationToken cancellationToken);
    Task SelectAllAsync(CancellationToken cancellationToken);
    Task TypeTextAsync(string text, CancellationToken cancellationToken);
    Task ReplaceTextAsync(string text, CancellationToken cancellationToken);
    Task PressEnterAsync(CancellationToken cancellationToken);
}
