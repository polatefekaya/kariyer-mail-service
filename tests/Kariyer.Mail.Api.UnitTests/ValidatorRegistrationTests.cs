using FluentValidation;
using Kariyer.Mail.Api.Common.Web.Filters;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kariyer.Mail.Api.UnitTests;

/// <summary>
/// Guards a failure that is invisible until a real request arrives.
///
/// `Program.cs` registers validators with `AddValidatorsFromAssembly(...)`, whose
/// `includeInternalTypes` parameter defaults to FALSE. An `internal` validator is therefore
/// skipped without a word: the solution builds, the service boots, the endpoint maps — and the
/// first live POST returns a 400 reading "Unable to resolve service for type
/// IValidator&lt;T&gt; while attempting to activate ValidationFilter&lt;T&gt;", because the
/// filter cannot be constructed. SubmitLeadValidator shipped exactly that way.
///
/// Asserting over the whole assembly rather than one type, since the next validator someone
/// adds is as likely to be internal as that one was.
/// </summary>
public class ValidatorRegistrationTests
{
    [Fact]
    public void Every_validator_in_the_assembly_is_resolvable()
    {
        ServiceCollection services = new();
        // Exactly the call Program.cs makes — including its defaults, which is the point.
        services.AddValidatorsFromAssembly(typeof(Program).Assembly);

        using ServiceProvider provider = services.BuildServiceProvider();

        (Type Implementation, Type ValidatorInterface)[] validators = typeof(Program).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            .Select(t => (
                Implementation: t,
                ValidatorInterface: t.GetInterfaces().FirstOrDefault(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>))!))
            .Where(x => x.ValidatorInterface is not null)
            .ToArray();

        // If this ever trips, the discovery above stopped finding anything and the rest of the
        // test would pass vacuously.
        Assert.NotEmpty(validators);

        string[] unresolvable = validators
            .Where(v => provider.GetService(v.ValidatorInterface) is null)
            .Select(v => $"{v.Implementation.Name} (is it `internal`?)")
            .ToArray();

        Assert.True(
            unresolvable.Length == 0,
            "These validators are not registered, so any endpoint using ValidationFilter<T> for "
            + $"them will 400 on its first request: {string.Join(", ", unresolvable)}");
    }

    [Fact]
    public void ValidationFilter_can_actually_be_constructed_for_every_validator()
    {
        // One step further than resolving the validator: ValidationFilter<T> is what the
        // endpoint pipeline actually activates, and it is the thing that threw.
        ServiceCollection services = new();
        services.AddValidatorsFromAssembly(typeof(Program).Assembly);

        using ServiceProvider provider = services.BuildServiceProvider();

        Type[] requestTypes = typeof(Program).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>))
            .Select(i => i.GetGenericArguments()[0])
            .Distinct()
            .ToArray();

        foreach (Type requestType in requestTypes)
        {
            Type filterType = typeof(ValidationFilter<>).MakeGenericType(requestType);
            object filter = ActivatorUtilities.CreateInstance(provider, filterType);
            Assert.NotNull(filter);
        }
    }
}
