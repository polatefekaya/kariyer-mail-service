namespace Kariyer.Mail.Api.Features.JobAlert;

/// <summary>
/// One candidate's job-alert digest is ready to be emailed.
///
/// Exchange: <c>job.alert.ready</c> (durable fanout, empty routing key), published by
/// kariyer_zamani_backend's <c>src/modules/jobAlert</c> once per employee per digest.
///
/// DEFINED HERE RATHER THAN IN Kariyer.Messaging.Contracts, deliberately and temporarily:
/// this project pins Contracts 1.0.2 while the package is already at 1.2.1, and adding a
/// type there would require publishing a new version and bumping the pin before this
/// feature could compile at all. Move it to <c>Kariyer.Messaging.Contracts.JobAlert</c> on
/// the next publish and delete this file — the shape is already the house one.
///
/// THE PAYLOAD DELIBERATELY CARRIES NO JOB LIST. The email says how many matches there are
/// and links to /is-uyarilarim, where the page shows them and marks the new ones. That is a
/// product decision (the page stays correct as listings close, and can show far more than
/// an email should) and a technical one: <c>DispatchEmailCommand.TemplateData</c> is
/// <c>Dictionary&lt;string, string&gt;</c>, so there is no way to pass a collection into a
/// Scriban template without changing that contract.
///
/// RECIPIENTS ARE ALREADY FILTERED. The publisher only emits for employees who granted
/// commercial-message consent (<c>ticari_elektronik_ileti_accepted</c>), have alerts
/// switched on, are not "not looking", and are not frozen, deleted or mid-deletion. This
/// consumer must not assume it can re-check any of that — it has no access to employee
/// records — and must not be reused for an audience assembled anywhere else.
/// </summary>
/// <param name="MessageId">
/// Publisher-minted idempotency key, deliberately not the broker envelope's id: that
/// changes on redelivery, so a consumer deduplicating on it would act twice.
/// </param>
/// <param name="Uid">Internal employee uid, used as the EmailTarget's UserId.</param>
/// <param name="Email">Already decrypted by the publisher.</param>
/// <param name="FullName">Display name, falling back to the address when there is no name.</param>
/// <param name="JobCount">How many new matches the digest covers.</param>
/// <param name="Slot">
/// Which send window the digest was cut in: <c>morning</c>, <c>noon</c> or <c>evening</c> —
/// or <c>always</c> when the publisher has every window disabled. Selects the template, so
/// the copy can suit the hour it lands in. An unrecognised or absent value falls back to
/// the morning template rather than failing the send.
/// </param>
/// <param name="AlertUrl">Absolute link to the İş Uyarılarım page.</param>
/// <param name="UnsubscribeUrl">
/// Absolute one-click unsubscribe link, valid without a session. NULL when the publisher
/// has no signing secret configured — a template must degrade rather than render a dead
/// link, so it is optional here and defaulted to the alerts page below.
/// </param>
/// <param name="GeneratedAt">ISO-8601 timestamp of the digest run.</param>
public sealed record JobAlertReadyEvent(
    string MessageId,
    string Uid,
    string Email,
    string FullName,
    int JobCount,
    string? Slot,
    string AlertUrl,
    string? UnsubscribeUrl,
    string GeneratedAt
);
