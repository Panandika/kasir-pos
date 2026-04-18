#nullable enable
using Kasir.Models;

namespace Kasir.Auth;

/// <summary>
/// Shared session holder for the logged-in user. Populated by LoginView after
/// successful authentication; read by any view that needs current-user context
/// (SaleView, CashReceipt, CashDisbursement, etc.). Cleared on logout.
/// </summary>
public static class CurrentSession
{
    public static User? User { get; set; }

    public static void Clear() => User = null;
}
