namespace HanyOptics.BusinessLogic.Interfaces;

// The business `users`.user_id of whoever is driving the current request. Every SP that
// stamps an actor (created_by / changed_by / received_by / recorded_by) gets its value
// from here instead of a hardcoded id.
//
// Implemented by the host (HanyOptics.Web) because only it knows about HttpContext - this
// interface stays free of any ASP.NET types so BusinessLogic keeps no dependency on the
// web layer.
public interface ICurrentUser
{
    // Null when there is no authenticated user (background work, or a request that never
    // passed [Authorize]).
    int? UserId { get; }

    // For code paths that are only reachable behind [Authorize] and genuinely cannot
    // proceed without an actor. Throws rather than silently falling back to some default
    // id, because a wrong actor is worse than a failed operation.
    int RequireUserId();
}
