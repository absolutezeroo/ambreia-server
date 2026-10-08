using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Authentication;
using Vortex.Tests.Support;
using Vortex.WebApi.Configuration;
using Vortex.WebApi.Services;
using Vortex.WebApi.Session;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// The site's error codes for a refused registration. The rules are the authenticator's
/// (<c>AccountAuthenticatorTests</c>); this pins that every outcome it can answer has its own code,
/// so a new outcome cannot reach the site disguised as another.
/// </summary>
public sealed class WebApiAuthServiceTests
{
    [Theory]
    [InlineData(AccountCreationOutcome.PasswordTooShort, "pocket.auth.password_too_short")]
    [InlineData(AccountCreationOutcome.PasswordTooLong, "pocket.auth.password_too_long")]
    [InlineData(AccountCreationOutcome.EmailTaken, "pocket.auth.email_already_taken")]
    public async Task ARefusal_HasItsCode(AccountCreationOutcome outcome, string code)
    {
        (bool success, int accountId, string? error) = await Service(outcome)
            .RegisterAsync("a@b.co", "password", CancellationToken.None);

        success.Should().BeFalse();
        accountId.Should().Be(0);
        error.Should().Be(code);
    }

    [Fact]
    public async Task EveryRefusal_IsMapped()
    {
        foreach (
            AccountCreationOutcome outcome in Enum.GetValues<AccountCreationOutcome>()
                .Where(o => o != AccountCreationOutcome.Created)
        )
        {
            Func<Task> register = () =>
                Service(outcome).RegisterAsync("a@b.co", "password", CancellationToken.None);

            await register.Should().NotThrowAsync("{0} needs a code", outcome);
        }
    }

    [Fact]
    public async Task ACreation_AnswersTheAccount()
    {
        (bool success, int accountId, string? error) = await Service(AccountCreationOutcome.Created)
            .RegisterAsync("a@b.co", "password", CancellationToken.None);

        success.Should().BeTrue();
        accountId.Should().Be(7);
        error.Should().BeNull();
    }

    private static WebApiAuthService Service(AccountCreationOutcome outcome) =>
        new(
            FakeProxy.Create<IAccountAuthenticator>(call =>
                call.Method.Name == nameof(IAccountAuthenticator.CreateAccountAsync)
                    ? Task.FromResult(
                        outcome == AccountCreationOutcome.Created
                            ? AccountCreation.Created(7)
                            : AccountCreation.Refused(outcome)
                    )
                    : null
            ),
            FakeProxy.Create<IAuthenticationService>(_ => null),
            new WebApiSessionStore(Options.Create(new WebApiConfig())),
            NullLogger<WebApiAuthService>.Instance
        );
}
