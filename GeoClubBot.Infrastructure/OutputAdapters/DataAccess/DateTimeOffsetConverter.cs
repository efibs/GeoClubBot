using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Infrastructure.OutputAdapters.DataAccess;

public class DateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
    d => d.ToUniversalTime(),
    d => d.ToUniversalTime());