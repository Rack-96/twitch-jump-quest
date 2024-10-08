using Godot;

public partial class TeamScore : RichTextLabel
{
    string attemptedTeamAbbrev;
    public UNL.Team team;
    private SettingsManager settingsManager;
    private LevelManager levelManager;
    public int teamScore = 0;

    public override void _Ready()
    {
        settingsManager = GetNode<SettingsManager>("/root/SettingsManager");
        levelManager = GetNode<LevelManager>("/root/LevelManager");
    }

    public void Initialise(string TeamAbbrev)
    {
            UNL.Team teamAbbrevTeam = settingsManager.UNLTeams.Teams.Find(team => team.TeamAbbreviation.ToLower() == TeamAbbrev.ToLower());
            
            if (teamAbbrevTeam == null)
            {
                return;
            }
            team = teamAbbrevTeam;
            AddThemeColorOverride("font_outline_color", Color.FromHtml(team.HexColourCode));
            Text = $"{team.TeamAbbreviation} [{levelManager.teamScores.GetTeamPlayerCount(team.TeamAbbreviation)}] - 0000";
}

    public void UpdateScore(int score, int playerCount)
    {
        // Text = $"{TeamAbbrev}[color=yellow][{playerCount}][/color]: [b]{score}[/b]";
        Text = $"{team.TeamAbbreviation} [{playerCount}] - {score}";
        teamScore = score;
    }
}