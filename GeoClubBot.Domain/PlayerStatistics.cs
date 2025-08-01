namespace Entities;

public record PlayerStatistics(
    string Nickname,
    DateTimeOffset HistorySince,
    int NumHistoryEntries,
    double AveragePoints,
    int MinPoints,
    int FirstQuartilePoints,
    int MedianPoints,
    int ThirdQuartilePoints,
    int MaxPoints);