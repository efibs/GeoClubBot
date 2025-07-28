namespace UseCases.OutputPorts.GeoGuessr.DTOs;

public record GeoGuessrChallengeResultHighscores(List<GeoGuessrChallengeResultItem> Items);

public record GeoGuessrChallengeResultItem(GeoGuessrChallengeResultGame Game);

public record GeoGuessrChallengeResultGame(GeoGuessrChallengeResultPlayer Player);

public record GeoGuessrChallengeResultPlayer(string Nick, GeoGuessrChallengeResultPlayerScore TotalScore, GeoGuessrChallengeResultPlayerDistance TotalDistance);

public record GeoGuessrChallengeResultPlayerScore(string Amount, string Unit);

public record GeoGuessrChallengeResultPlayerDistance(GeoGuessrChallengeResultPlayerDistanceMeters Meters);

public record GeoGuessrChallengeResultPlayerDistanceMeters(string Amount, string Unit);