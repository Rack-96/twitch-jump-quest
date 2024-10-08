using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Vector2 = Godot.Vector2;
using UNLTeamJumpQuest.TwitchIntegration;
using TwitchLib.Api.Helix.Models.Games;

public partial class Player : CharacterBody2D
{
    [Signal]
    public delegate void DiedEventHandler(string displayName, string userID, string teamAbbrev);
    [Signal]
    public delegate void FaceplantedEventHandler(Player player, float fallDistance);
    [Signal]
    public delegate void ComboStreakingEventHandler(Player player, int comboStreak);
    [Signal]
    public delegate void ScoreUpdatedEventHandler(string teamAbbrev, int points);
	AnimatedSprite2D animatedSprite;
    Label displayLabel;

    [Export]
    public float distanceForHeadOnFloor = 250;
    public float BaseJumpVelocity = 500.0f;
    public float Gravity = 980.0f; 
    public bool headOnFloor = false;
    private float currentYPos = 0, previousYPos = 0, highestYPos = 0, jumpYPos = 0;
    private const float TILE_SIZE = 16f;
    private Vector2 lastScoredPosition;
    private int currentPlatformId = -1;

    private Vector2 _jumpVelocity = Vector2.Zero;

    public string userID, displayName;
    private UNL.Team team;
    private Color[] teamColours;
    private DebugTwitchChat debugger;
    private SettingsManager settingsManager;
    private LevelManager levelManager;
    public int points, combo = 0, comboStreak, numOfFaceplants = 0, distanceOfFurthestFaceplant = 0, idxOfUniqueFeatherColour;
    private const int MAX_TRACKED_PLATFORMS = 10;
    private Queue<PlatformInfo> recentPlatforms = new Queue<PlatformInfo>();
    private float highestYPosition = float.MaxValue; // Remember, lower Y is higher in Godot

    public struct PlatformInfo
    {
        public int PlatformId { get; }
        public float XPosition { get; }
        public float YPosition { get; }

        public PlatformInfo(int platformId, float x, float y)
        {
            PlatformId = platformId;
            XPosition = x;
            YPosition = y;
        }
    }

    private Label DEBUG_LABEL;
    private Label DEBUG_COMBO;
    private GameManager gameManager;

    public void Initialize(string _displayName, string _userID, UNL.Team _team)
    {
        displayName = _displayName;
        userID = _userID;
        points = 0;
        team = _team;
        SetColoursArray(_team);
        if (_team == null)
        {
            return;
        }
    }
    public override async void _Ready()
    {
        base._Ready();

        debugger = GetNodeOrNull<DebugTwitchChat>("/root/Main/CanvasLayer/DebugTwitchChat");
        settingsManager = GetNodeOrNull<SettingsManager>("/root/SettingsManager");
        levelManager = GetNodeOrNull<LevelManager>("/root/LevelManager");
        gameManager = GetNodeOrNull<GameManager>("/root/GameManager");
        

        if (debugger != null)
        {
            debugger.DebuggerDeleteSelf += OnDeleteSelf;
        }

		animatedSprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
        displayLabel = GetNode<Label>("DisplayNameLabel");
        displayLabel.Text = displayName;
        ShaderMaterial uniqueMaterial = (ShaderMaterial)animatedSprite.Material.Duplicate();
        SetTeamColours(teamColours, uniqueMaterial);
        animatedSprite.Material = uniqueMaterial;

        TwitchBot.Instance.MessageReceived += OnMessageReceived;
        animatedSprite.AnimationFinished += OnHeadOnFloorAnimationFinished;

        if (userID == "DEBUG")
        {
            AddToGroup("DebugPlayer");
        }
        else
            AddToGroup("Player");

        DEBUG_LABEL = GetNode<Label>("DEBUG_POINTS_LABEL");
        DEBUG_COMBO = GetNode<Label>("DEBUG_COMBO_LABEL");
        // Some reason it won't change in Player scene so I do it through code.
        SetCollisionLayerValue(1, false);
        SetCollisionLayerValue(2, true);
        await showDisplayName(3.5);
    }

    private void CalculateScore()
    {
        if (gameManager.CurrentGameState != GameManager.GameState.Playing)
        {
            return;
        }
        
        var (newPlatformId, layerName) = GetCurrentPlatformId();
        Vector2 currentPosition = GlobalPosition;

        if (newPlatformId == -1 || layerName != "Midground")
        {
            return; // Not on a platform or not on Midground
        }

        bool isNewHighestPosition = currentPosition.Y < highestYPosition;
        bool isSameHeight = Math.Abs(currentPosition.Y - highestYPosition) < TILE_SIZE;
        bool isNewPlatform = !recentPlatforms.Any(p => 
            p.PlatformId == newPlatformId);
        
        // GD.Print($"newPlatfromId: {newPlatformId}");
        // GD.Print($"isNewHighestPosition: {isNewHighestPosition}, isSameHeight: {isSameHeight}, isNewPlatform: {isNewPlatform}");
        // GD.Print($"currentPosition: {currentPosition}, highestYPosition: {highestYPosition}");
        // GD.Print($"Recent platforms: {string.Join(", ", recentPlatforms.Select(p => $"(ID:{p.PlatformId},{p.XPosition}, {p.YPosition})"))}");

        // Calculate points
        float progressFraction = (levelManager.startMarkerYPos - currentPosition.Y) / levelManager.totalLevelYDistance;
        int pointsGained = Mathf.FloorToInt(progressFraction * 100);

        // bool shouldAwardPointsAndIncreaseCombo = (isNewHighestPosition || (isSameHeight && isNewPlatform)) && pointsGained > 0;
        bool shouldAwardPointsAndIncreaseCombo = ((isNewHighestPosition || isSameHeight) && isNewPlatform) && pointsGained > 0;

        if (shouldAwardPointsAndIncreaseCombo)
        {
            combo++;
            DEBUG_COMBO.Text = $"combo: {combo}";

            AddScore(pointsGained);
            DEBUG_LABEL.Visible = true;
            DEBUG_LABEL.Text = $"+{pointsGained}";
            // GD.Print($"Points given for Midground platform: {pointsGained}");

            recentPlatforms.Enqueue(new PlatformInfo(newPlatformId, currentPosition.X, currentPosition.Y));
            if (recentPlatforms.Count > MAX_TRACKED_PLATFORMS)
            {
                recentPlatforms.Dequeue();
            }

    if (isNewPlatform)
            {
                recentPlatforms.Enqueue(new PlatformInfo(newPlatformId, currentPosition.X, currentPosition.Y));
                if (recentPlatforms.Count > MAX_TRACKED_PLATFORMS)
                {
                    recentPlatforms.Dequeue();
                }
            }

            if (isNewHighestPosition)
            {
                highestYPosition = currentPosition.Y;
            }

            lastScoredPosition = currentPosition;
        }
        else
        {
            combo = 0;
            DEBUG_COMBO.Text = $"combo: {combo}";
        }
    }

    private (int platformId, string layerName) GetCurrentPlatformId()
    {
        var spaceState = GetWorld2D().DirectSpaceState;
        uint collisionMask = 1; // Assuming your jumpable tiles are on collision layer 1

        Vector2[] directions = { Vector2.Down, new Vector2(-1, 1), new Vector2(1, 1) };
        float rayLength = 3f; // Adjust this value as needed

        foreach (var direction in directions)
        {
            var query = PhysicsRayQueryParameters2D.Create(
                GlobalPosition,
                GlobalPosition + direction * rayLength,
                collisionMask,
                new Godot.Collections.Array<Rid> { GetRid() } // Exclude the player's own collision
            );

            var result = spaceState.IntersectRay(query);

            if (result.Count > 0)
            {
                var collider = result["collider"].As<Node2D>();
                if (collider is TileMap tileMap)
                {
                    Vector2 collisionPoint = (Vector2)result["position"];
                    Vector2I cellCoords = tileMap.LocalToMap(tileMap.ToLocal(collisionPoint));
                    
                    string layerName = tileMap.GetLayerName(levelManager.midgroundLayerId);
                    if (tileMap.GetCellSourceId(levelManager.midgroundLayerId, cellCoords) != -1)
                    {
                        int platformId = levelManager.GetPlatformId(cellCoords);
                        GD.Print($"Player at cell {cellCoords}, Platform ID: {platformId}");
                        return (platformId, layerName);
                    }
                }
            }
        }
        GD.PrintErr("No valid platform found near player");
        return (-1, string.Empty);
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector2 velocity = Velocity;

        if (!IsOnFloor())
        {
            velocity.Y += Gravity * (float)delta;
            animatedSprite.Play("Jump");

            // If jumping up or is there is an x velocity during a jump then set highest y position while not on the floor
            var goingHorizontal = Math.Abs(velocity.X) > Math.Abs(velocity.Y); // Takes the highest even thought going horizonal
            if (velocity.Y < 0 && GlobalPosition.Y < jumpYPos || goingHorizontal)
            {
                highestYPos = GlobalPosition.Y;
            }
            jumpYPos = GlobalPosition.Y;

            // If collided with wall set highest vector.y to previous platform.y if platform.y is higher then highestYpos
            if(IsOnWall())
            {
                // If highestYPos is lowerer then platformYPos, then use the height from the last platformYPos
                if (highestYPos > previousYPos)
                {
                    highestYPos = previousYPos;
                }
            }
        }

        if (IsOnFloor())
        {
            if (lastScoredPosition == Vector2.Zero)
            {
                lastScoredPosition = GlobalPosition;
                GD.Print($"Initial landing position set: {lastScoredPosition}");
            }

            currentYPos = GlobalPosition.Y;
            if (highestYPos != 0)
            {
                if (!IsOnCeiling() && !IsOnWall())
                {
                    CalculateScore();
                }
                
                float heightDifference = Math.Abs(currentYPos) - Math.Abs(highestYPos);
                if (heightDifference >= distanceForHeadOnFloor)
                {
                    headOnFloor = true;
                }
                highestYPos = 0;
            }

            if (headOnFloor)
                animatedSprite.Play("HeadOnFloor");
            else
                animatedSprite.Play("Idle");

            // Apply jump velocity if there's a pending jump
            if (_jumpVelocity != Vector2.Zero)
            {
                previousYPos = currentYPos;
                // _ = stop warning error :)
                _ = showDisplayName(2.0);
                velocity = _jumpVelocity;
                _jumpVelocity = Vector2.Zero; // Reset jump velocity after applying
            }
            else
            {
                velocity.X = 0; // Stop horizontal movement when on floor and not jumping
            }
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    public void ResetPlayerState()
    {
        points = 0;
        combo = 0;
        comboStreak = 0;
        numOfFaceplants = 0;
        distanceOfFurthestFaceplant = 0;
        
        currentYPos = GlobalPosition.Y;
        previousYPos = GlobalPosition.Y;
        highestYPos = 0;
        jumpYPos = GlobalPosition.Y;
        highestYPosition = float.MaxValue;
        
        headOnFloor = false;
        lastScoredPosition = Vector2.Zero;
        currentPlatformId = -1;
        
        Velocity = Vector2.Zero;
        _jumpVelocity = Vector2.Zero;
        
        recentPlatforms.Clear();
        
        DEBUG_LABEL.Visible = false;
        DEBUG_COMBO.Text = "combo: 0";
        
        animatedSprite.Play("Idle");
        animatedSprite.FlipH = false;
    }


    public void Die()
    {
        EmitSignal(SignalName.Died, displayName, userID, team.TeamAbbreviation);
        // death logic, play animation, remove from scene
    }

    public void AddScore(int points)
    {
        if (team == null)
            return;
        this.points += points;
        EmitSignal(SignalName.ScoreUpdated, team.TeamAbbreviation, points);
    }

    private void OnHeadOnFloorAnimationFinished()
    {
        if (animatedSprite.Animation == "HeadOnFloor")
        {
            currentYPos = 0;
            previousYPos = 0;
            headOnFloor = false;
        }
    }

    private void OnDeleteSelf()
    {
        if (userID == "DEBUG")
        {
            GameManager.Instance.DeletePlayer("DEBUG");
        }
        
    }

    private void OnDebuggerCommandsGiven(float angle, float power)
    {
        GD.Print($"{displayName} has {angle} degrees and {power} power, jumpvelocity {BaseJumpVelocity}");
        if (userID != "DEBUG")
            return;
        DoJumpPhysics(angle, power);
    }

    public void DoJumpPhysics(float angle, float power)
    {
        if (headOnFloor)
            return;

        _jumpVelocity = MyMaths.SetJumpVector(angle, power, BaseJumpVelocity);
        
        if (_jumpVelocity.X < 0)
            animatedSprite.FlipH = true;
        else
            animatedSprite.FlipH = false;
    }

    private void OnMessageReceived(string[] messageInfo)
    {
        if (!IsOnFloor()) return; // Only process jump commands when on the floor

        if (messageInfo[1] != userID)
            return;

        var (isValid, angle, power) = MessageParser.ParseMessage(messageInfo[2]);
        if (isValid)
        {
            DoJumpPhysics(angle, power);
        }
    }

    public string GetUserID()
    {
        return userID;
    }

    private async Task showDisplayName(double time)
    {
        displayLabel.Visible = true;
        await ToSignal(GetTree().CreateTimer(time), "timeout");
        displayLabel.Visible = false;
    }

    public void SetTeamColours(Color[] colourArray, ShaderMaterial uniqueMaterial)
    {
        // int teamPlayerCount = levelManager.teamScores.GetTeamPlayerCount(team.TeamAbbreviation);
        uniqueMaterial.SetShaderParameter("cape1_color_new", colourArray[0]);
        uniqueMaterial.SetShaderParameter("cape2_color_new", colourArray[1]); 
        uniqueMaterial.SetShaderParameter("armour_dark_new", colourArray[2]);
        uniqueMaterial.SetShaderParameter("armour_med_new", colourArray[3]);
        uniqueMaterial.SetShaderParameter("armour_light_new", colourArray[4]);
        if (displayName == "DEBUG")
        {
            return;
        }
        uniqueMaterial.SetShaderParameter("helmet_feathers_new", 
            levelManager.uniqueColours[(levelManager.teamScores.GetTeamPlayerCount(team.TeamAbbreviation) - 1) % 15]);
        idxOfUniqueFeatherColour = ( levelManager.teamScores.GetTeamPlayerCount(team.TeamAbbreviation) - 1 ) % 15;
    }

    private void SetColoursArray(UNL.Team team)
    {
        teamColours = new Color[5];
        if (team == null)
        {
            teamColours[0] = Color.FromHtml("#fff");
            teamColours[1] = Color.FromHtml("#eba724");
            teamColours[2] = Color.FromHtml("#d2202c");
            teamColours[3] = Color.FromHtml("#7c776f");
            teamColours[4] = Color.FromHtml("#eadfd1");
            return;
        }
        teamColours[0] = team.TeamColours.CapeMain;
        teamColours[1] = team.TeamColours.CapeTrim;
        teamColours[2] = team.TeamColours.ArmourLight;
        teamColours[3] = team.TeamColours.ArmourMedium;
        teamColours[4] = team.TeamColours.ArmourDark;
    }
}
