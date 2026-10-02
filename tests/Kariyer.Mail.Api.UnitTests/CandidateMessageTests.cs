using Kariyer.Mail.Api.Features.Recruiting.CandidatesMessaged;
using Xunit;

namespace Kariyer.Mail.Api.UnitTests;

public class CandidateMessageTests
{
    [Fact]
    public void A_companys_text_cannot_become_markup()
    {
        // Scriban inserts values as they are; a recruiter's "<" must arrive as text.
        Assert.Equal(
            "Maaş &lt; 50.000 &amp; &lt;b&gt;uzaktan&lt;/b&gt;",
            CandidatesMessagedConsumer.AsHtml("Maaş < 50.000 & <b>uzaktan</b>"));
    }

    [Fact]
    public void Line_breaks_survive_as_breaks()
    {
        Assert.Equal("Merhaba,<br><br>Teşekkürler.", CandidatesMessagedConsumer.AsHtml("  Merhaba,\r\n\nTeşekkürler.  "));
    }
}
