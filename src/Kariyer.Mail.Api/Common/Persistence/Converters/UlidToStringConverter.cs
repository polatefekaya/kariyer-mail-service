using System;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Kariyer.Mail.Api.Common.Persistence.Converters;

public sealed class UlidToStringConverter : ValueConverter<Ulid, string>
{
    private static readonly ConverterMappingHints _defaultHints = new (size: 26);

    public UlidToStringConverter() : base(
        ulid => ulid.ToString(),
        str => Ulid.Parse(str),
        _defaultHints)
    {
    }
}