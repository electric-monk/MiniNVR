
namespace TestConsole.Configuration.Users
{
    public interface IProvider
    {
        /// <summary>
        /// User-readable name of this authentication method.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// A direct link (or null if the HTML is required to proceed).
        /// </summary>
        string DirectLink { get; }

        /// <summary>
        /// HTML to display to use this authentication method, if necessary.
        /// </summary>
        string HTML { get; }

        /// <summary>
        /// A direct link to a management UI page, if present.
        /// </summary>
        string ManagementLink { get; }
    }
}
