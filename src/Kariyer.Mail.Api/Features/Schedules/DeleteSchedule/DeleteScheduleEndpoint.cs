using Hangfire;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Schedules.DeleteSchedule;

internal sealed class DeleteScheduleEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("schedules/{id:ulid}", async (
            Ulid id,
            MailDbContext dbContext,
            IRecurringJobManager recurringJobs,
            IConnectionMultiplexer multiplexer,
            CancellationToken ct) =>
        {
            int updatedRows = await dbContext.EmailJobSchedules
                .Where(s => s.Id == id && s.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), ct);
                
            if (updatedRows == 0) return Results.NotFound();

            recurringJobs.RemoveIfExists(id.ToString());
            
            IDatabase garnet = multiplexer.GetDatabase();
            await garnet.KeyDeleteAsync("schedules:all:inactive_false");
            await garnet.KeyDeleteAsync("schedules:all:inactive_true");
            await garnet.KeyDeleteAsync($"schedule:detail:{id}");
            
            return Results.NoContent();
        })
        .WithTags("Schedules");
    }
}