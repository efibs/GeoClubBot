namespace UseCases.OutputPorts.GeoGuessr.DTOs;

public record GeoGuessrChallengeResultHighscores(List<GeoGuessrChallengeResultGame> Items);

public record GeoGuessrChallengeResultGame(GeoGuessrChallengeResultPlayer Player);

public record GeoGuessrChallengeResultPlayer(string Nick, GeoGuessrChallengeResultPlayerScore TotalScore, GeoGuessrChallengeResultPlayerDistance TotalDistance);

public record GeoGuessrChallengeResultPlayerScore(string Amount, string Unit);

public record GeoGuessrChallengeResultPlayerDistance(GeoGuessrChallengeResultPlayerDistanceMeters Meters);

public record GeoGuessrChallengeResultPlayerDistanceMeters(string Amount, string Unit);