
namespace TestConsole.Configuration.Users
{
    /// <summary>
    /// Represents an active user session
    /// </summary>
    public interface ISession
    {
        string Identifier { get; }

        /// <summary>
        /// Get the display username for this session.
        /// </summary>
        string Username { get; }

        /// <summary>
        /// Get the groups this session belongs to.
        /// </summary>
        string[] Groups { get; }

        /// <summary>
        /// Check if this session is still usable. Allowed to trigger network access.
        /// </summary>
        bool IsValid { get; }

        /// <summary>
        /// Get the authentication mechanism responsible for this session.
        /// </summary>
        IProvider AuthenticationProvider { get; }
    }
}
