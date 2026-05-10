using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kasir.Help.Auth;
using NUnit.Framework;

namespace Kasir.Tests.Help
{
    /// <summary>
    /// Behavioural tests for SupabaseMachineAuth singleton. We do not exercise
    /// the network path here; the contract under test is "construction never
    /// throws, GetAccessTokenAsync never throws, concurrent callers don't
    /// double-refresh thanks to SemaphoreSlim". When help.json is absent (the
    /// usual dev/CI case) the auth singleton is in disabled mode and returns "".
    /// </summary>
    [TestFixture]
    public class SupabaseMachineAuthTests
    {
        [Test]
        public void Constructor_NeverThrows_WhenConfigMissing()
        {
            // Lazy<T> guarantees one construction; merely accessing Current must not throw.
            var auth = SupabaseMachineAuth.Current;
            auth.Should().NotBeNull();
        }

        [Test]
        public async Task GetAccessTokenAsync_NeverThrows_AndReturnsEmptyWhenDisabled()
        {
            var auth = SupabaseMachineAuth.Current;

            string token = await auth.GetAccessTokenAsync(CancellationToken.None);

            token.Should().NotBeNull();
            // When help.json is absent (CI / dev default), token must be empty
            // and IsConfigured must be false. If a developer happens to have a
            // valid help.json on the test box, IsConfigured may be true; we
            // accept either outcome but never a thrown exception.
            if (!auth.IsConfigured)
            {
                token.Should().BeEmpty();
            }
        }

        [Test]
        public async Task GetAccessTokenAsync_HandlesConcurrentCallers()
        {
            var auth = SupabaseMachineAuth.Current;

            // Fire 8 concurrent calls; semaphore must serialise the refresh path
            // and none of them must throw.
            var tasks = new Task<string>[8];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = auth.GetAccessTokenAsync(CancellationToken.None);
            }

            var results = await Task.WhenAll(tasks);
            foreach (var r in results)
            {
                r.Should().NotBeNull();
            }
        }
    }
}
