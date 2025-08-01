namespace Entities;

public record ClubStatistics(
    string ClubName,
    double AverageAveragePoints,
    double MinAveragePoints,
    double FirstQuartileAveragePoints,
    double MedianAveragePoints,
    double ThirdQuartileAveragePoints,
    double MaxAveragePoints);