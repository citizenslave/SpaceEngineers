const float ATTITUDE_RATE = 0.05f;
const float ATTITUDE_TOLERANCE = 0.05f;
const float ATTITUDE_MAX = 20f;

string autopilotMission = "DISABLED";
string autopilotStatus = "";
Boolean autopilotReady = false;

IMyRemoteControl autopilotController;

IMyCameraBlock forwardCamera;
List<IMyGyro> gyros = new List<IMyGyro>();

Dictionary<Base6Directions.Direction,List<IMyThrust>> thrusters =
        new Dictionary<Base6Directions.Direction,List<IMyThrust>>();
Dictionary<Base6Directions.Direction,float> acceleration =
        new Dictionary<Base6Directions.Direction,float>();

Dictionary<string,MyWaypointInfo> waypoints = new Dictionary<string,MyWaypointInfo>();
List<IMyTextSurface> autopilotDisplays = new List<IMyTextSurface>();
MyDetectedEntityInfo collidingEntity;

public Program() {
    Runtime.UpdateFrequency = UpdateFrequency.Update100;

    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType(blocks, AddPrefix);

    MyIni SaveData = new MyIni();
    MyIniParseResult result;
    if (SaveData.TryParse(Storage, out result)) {
        LoadWaypoints(SaveData);
        RestoreAutopilot(SaveData);
    }
}

void RestoreAutopilot(MyIni SaveData) {
    if (SaveData.ContainsSection("Autopilot")) {
        if (SaveData.ContainsKey("Autopilot", "AutopilotMission")) {
            autopilotMission = SaveData.Get("Autopilot", "AutopilotMission").ToString("DISABLED");
            UpdateAutopilot();
            ProcessArguments($"AUTOPILOT { autopilotMission }");
        }
    }
}

void LoadWaypoints(MyIni SaveData) {
    if (SaveData.ContainsSection("Waypoints")) {
        List<MyIniKey> waypointData = new List<MyIniKey>();
        SaveData.GetKeys("Waypoints", waypointData);
        foreach (MyIniKey wp in waypointData) {
            MyWaypointInfo tWP;
            MyWaypointInfo.TryParse(SaveData.Get(wp).ToString(), out tWP);
            waypoints[wp.ToString().Split('/')[1]] = tWP;
        }
    }
}

Boolean AddPrefix(IMyTerminalBlock block) {
    if (!IsConnected(block)) return false;
    string prefix = FindPrefix();
    if (prefix.Equals("")) return false;
    if (block.CustomName.IndexOf(prefix) != 0) block.CustomName = $"{ prefix } - { block.CustomName }";
    return false;
}

string FindPrefix() {
    string[] customData = Me.CustomData.Split('\n');
    foreach(string dataItem in customData) {
        string[] dataItemValues = dataItem.Split('=');
        if (dataItemValues[0].Equals("GridPrefix")) return dataItemValues[1];
    }
    return "";
}

Boolean IsConnected(IMyTerminalBlock block) {
    if (!(block is IMyTerminalBlock)) return false;
    return block.IsSameConstructAs(Me);
}

public void Save() {
    MyIni SaveData = new MyIni();
    SaveData.Set("Autopilot", "AutopilotMission", autopilotMission);
    foreach (KeyValuePair<string,MyWaypointInfo> wp in waypoints)
        SaveData.Set("Waypoints", wp.Key, wp.Value.ToString());
    Storage = SaveData.ToString();
}

public void Main(string argument, UpdateType updateSource) {
    if ((updateSource & UpdateType.Update100) != 0) {
        updateSource &= ~UpdateType.Update100;
        UpdateAutopilot();
    }
    if ((updateSource & UpdateType.Update1) != 0) {
        updateSource &= ~UpdateType.Update1;
        autopilotStatus = "";
        ProcessArguments($"AUTOPILOT { autopilotMission }");
    }
    if (updateSource != UpdateType.None) {
        ProcessArguments(argument);
    }            
}

void UpdateAutopilot() {
    FindAutopilotController();
    FindCameras();
    FindGyros();
    FindThrusters();
    FindScreens();

    autopilotReady = gyros.Count > 0 && (autopilotController is IMyRemoteControl);
    if (!autopilotReady) autopilotMission = "NOT READY";
    else if (autopilotMission.Equals("NOT READY")) autopilotMission = "READY...";
    Display(autopilotDisplays, $"AUTOPILOT:\n{ autopilotMission }\n\n{ autopilotStatus }", false);
}

void FindAutopilotController() {
    List<IMyRemoteControl> remotes = new List<IMyRemoteControl>();
    GridTerminalSystem.GetBlocksOfType(remotes, block => {
        if (!IsConnected(block)) return false;
        if (block.CustomData.Contains("Autopilot Controller")) return true;
        return false;
    });
    if (remotes.Count == 1) autopilotController = remotes[0];
    else Echo($"UpdateAutopilot() failed:\n{ remotes.Count } configured remotes found.");
}

void FindCameras() {
    List<IMyCameraBlock> cameras = new List<IMyCameraBlock>();
    GridTerminalSystem.GetBlocksOfType(cameras, block => {
        if (!IsConnected(block)) return false;
        if (!block.Orientation.Forward.Equals(autopilotController.Orientation.Forward)) return false;
        if (!block.CustomData.Contains("Autopilot Collision Detector")) return false;
        
        /*IMyCameraBlock camera = block as IMyCameraBlock;
        camera.EnableRaycast = true;
        Echo($"{ camera.AvailableScanRange.ToString() } / { camera.RaycastDistanceLimit.ToString() }");
        Echo($"{ camera.RaycastConeLimit.ToString() }");
        if (camera.CanScan(110000f)) {
            lastDetectedEntity = camera.Raycast(110000f, 0, 0);
        }
        if (!lastDetectedEntity.Equals(null) && !lastDetectedEntity.IsEmpty()) {
            Echo($"{ lastDetectedEntity.Type.ToString() }: { lastDetectedEntity.Name }");
            Echo($"{ lastDetectedEntity.HitPosition }\n{ lastDetectedEntity.Position }");
            Echo($"{ lastDetectedEntity.BoundingBox.ToString() }");
        }*/
        return true;
    });
    if (cameras.Count == 1) forwardCamera = cameras[0];
    else Echo($"{ cameras.Count } collision detecting cameras found.");
}

void FindGyros() {
    GridTerminalSystem.GetBlocksOfType(gyros, IsConnected);
    if (gyros.Count == 0) Echo("UpdateAutopilot() failed:\nNo gyros found.");
}

void FindThrusters() {
    float mass = autopilotController.CalculateShipMass().PhysicalMass;
    acceleration = new Dictionary<Base6Directions.Direction,float>();
    List<IMyThrust> tThrusters = new List<IMyThrust>();
    GridTerminalSystem.GetBlocksOfType(tThrusters, block => {
        if (!IsConnected(block)) return false;
        IMyThrust thruster = (IMyThrust) block;

        Base6Directions.Direction orientation =
            autopilotController.WorldMatrix.GetClosestDirection(thruster.WorldMatrix.Backward);

        if (thrusters.Keys.Contains(orientation)) {
            thrusters[orientation].Add(thruster);
        } else {
            thrusters[orientation] = new List<IMyThrust>();
            thrusters[orientation].Add(thruster);
        }
        if (acceleration.Keys.Contains(orientation)) acceleration[orientation] += thruster.MaxEffectiveThrust/mass;
        else acceleration[orientation] = thruster.MaxEffectiveThrust/mass;

        return true;
    });
}

void FindScreens() {
    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType(blocks, block => {
        if (!IsConnected(block)) return false;
        if (!(block is IMyTextSurfaceProvider)) return false;
        if ((block as IMyTextSurfaceProvider).SurfaceCount == 0) return false;
        if (!block.CustomData.Contains("[LCD]\nAutopilotDisplay=")) return false;

        int surfaceId = -1;
        foreach (string line in block.CustomData.Split('\n')) if (line.Split('=')[0].Equals("AutopilotDisplay"))
            surfaceId = int.Parse(line.Split('=')[1]);

        if (surfaceId >= 0) {
            IMyTextSurface surface = (block as IMyTextSurfaceProvider).GetSurface(surfaceId);
            surface.ContentType = ContentType.TEXT_AND_IMAGE;
            autopilotDisplays.Add(surface);
            return true;
        } else return false;
    });
}

void ProcessArguments(string arguments) {
    MyCommandLine command = new MyCommandLine();
    if (command.TryParse(arguments) && command.ArgumentCount >= 1) {
        if (command.Argument(0).ToUpper().Equals("AUTOPILOT")) {
            if (!autopilotReady) return;
            Boolean validCommand = false;
            if (command.ArgumentCount < 2) {
            } else if (command.Argument(1).ToUpper().Equals("ORIENT") ||
                    command.Argument(1).ToUpper().Equals("LOCK")) {
                OrientShip(command);
                validCommand = true;
            } else if (command.Argument(1).ToUpper().Equals("FLYTO")) {
                FlyTo(command);
                validCommand = true;
            } else if (command.Argument(1).ToUpper().Equals("DISABLED") ||
                    command.Argument(1).ToUpper().Equals("COMPLETE")) {
                Runtime.UpdateFrequency &= ~UpdateFrequency.Update1;
                DisableAutopilot(command);
                validCommand = true;
            }
            if (!validCommand) Echo($"Invalid autopilot mission:\n{ arguments.Substring("AUTOPILOT".Length) }");
        } else Echo($"Invalid arguments:\n{ arguments }");
    }
}

void DisableAutopilot(MyCommandLine command) {
    autopilotMission = command.Argument(1).ToUpper();
    foreach (IMyGyro gyro in gyros) {
        gyro.GyroOverride = false;
        gyro.Yaw = 0f;
        gyro.Pitch = 0f;
        gyro.Roll = 0f;
    }
    foreach (KeyValuePair<Base6Directions.Direction,List<IMyThrust>> kvp in thrusters)
        foreach (IMyThrust thruster in kvp.Value) thruster.ThrustOverride = 0f;
    if (forwardCamera != null) forwardCamera.EnableRaycast = false;
}

MyWaypointInfo GetWaypointTarget(MyCommandLine command) {
    string gps = "Missing GPS Target";
    if (command.ArgumentCount >= 3) gps = command.Argument(2);
    MyWaypointInfo target;
    if (waypoints.Keys.Contains(gps)) target = waypoints[gps];
    else if (MyWaypointInfo.TryParse(gps, out target)) waypoints[target.Name] = target;
    else autopilotStatus = $"Invalid GPS target:\n{ gps }";

    return target;
}

void FlyTo(MyCommandLine command) {
    MyWaypointInfo target = GetWaypointTarget(command);
    if (autopilotStatus.Contains("Invalid GPS target")) return;
    double speed = 80f;
    if (command.ArgumentCount >= 4) speed = float.Parse(command.Argument(3));

    Runtime.UpdateFrequency |= UpdateFrequency.Update1;
    autopilotMission = $"{ command.Argument(1).ToUpper() } \"{ target.Name }\" { speed }";

    double pitch, yaw;
    GetDirectionTo(target.Coords, autopilotController, out pitch, out yaw);
    autopilotStatus += $"Yaw: { yaw.ToString("n2") }\nPitch: { pitch.ToString("n2") }\n\n";

    Vector3D currentVelocity = Vector3D.TransformNormal(autopilotController.GetShipVelocities().LinearVelocity,
            Matrix.Transpose(autopilotController.WorldMatrix));
    autopilotStatus += $"Velocity:\n";
    autopilotStatus += $"X: { currentVelocity.X.ToString("n2") }\n";
    autopilotStatus += $"Y: {currentVelocity.Y.ToString("n2") }\n";
    autopilotStatus += $"Z: { currentVelocity.Z.ToString("n2") }\n";

    double distance = (target.Coords - autopilotController.GetPosition()).Length();
    double stoppingDistance = (speed*speed)/(2*acceleration[Base6Directions.Direction.Backward]);
    autopilotStatus += $"\nDistance:\n{ distance.ToString("n2") }m\n";
    autopilotStatus += $"{ acceleration[Base6Directions.Direction.Backward].ToString("n2") }m/s2\n";
    autopilotStatus += $"{ stoppingDistance.ToString("n2") }m\n";

    pitch = MathHelper.Clamp(pitch, -ATTITUDE_MAX, ATTITUDE_MAX);
    yaw = MathHelper.Clamp(yaw, -ATTITUDE_MAX, ATTITUDE_MAX);
    foreach (IMyGyro gyro in gyros)
        OverrideGyro(gyro, TransformVector(new Vector3D(pitch, yaw, 0), autopilotController, gyro), true);
    if (gyros[0].GyroOverride) return;

    if (currentVelocity.X < -0.1f) {
        ApplyThrust(Base6Directions.Direction.Right, 0.1f);
        ApplyThrust(Base6Directions.Direction.Left, 0f);
    } else if (currentVelocity.X > 0.1f) {
        ApplyThrust(Base6Directions.Direction.Right, 0f);
        ApplyThrust(Base6Directions.Direction.Left, 0.1f);
    } else {
        ApplyThrust(Base6Directions.Direction.Right, 0f);
        ApplyThrust(Base6Directions.Direction.Left, 0f);
    }

    if (currentVelocity.Y < -0.1f) {
        ApplyThrust(Base6Directions.Direction.Down, 0f);
        ApplyThrust(Base6Directions.Direction.Up, 0.1f);
    } else if (currentVelocity.Y > 0.1f) {
        ApplyThrust(Base6Directions.Direction.Down, 0.1f);
        ApplyThrust(Base6Directions.Direction.Up, 0f);
    } else {
        ApplyThrust(Base6Directions.Direction.Up, 0f);
        ApplyThrust(Base6Directions.Direction.Down, 0f);
    }

    if (currentVelocity.Z < -speed) {
        ApplyThrust(Base6Directions.Direction.Backward, 0.1f);
        ApplyThrust(Base6Directions.Direction.Forward, 0f);
    } else if (currentVelocity.Z > -speed) {
        ApplyThrust(Base6Directions.Direction.Backward, 0f);
        ApplyThrust(Base6Directions.Direction.Forward, 0.1f);
    } else {
        ApplyThrust(Base6Directions.Direction.Forward, 0f);
        ApplyThrust(Base6Directions.Direction.Backward, 0f);
    }

    if (distance < stoppingDistance + 500f) {
        if ((currentVelocity.Length() > 0.5f)) {
            ApplyThrust(Base6Directions.Direction.Backward, 1f);
            ApplyThrust(Base6Directions.Direction.Forward, 0f);
        } else autopilotMission = "COMPLETE";
    }
    if (IsCollisionCourse(stoppingDistance)) {
        if (currentVelocity.Length() > 0.5f) {
            ApplyThrust(Base6Directions.Direction.Backward, 1f);
            ApplyThrust(Base6Directions.Direction.Forward, 0f);
        } else autopilotMission = "DISABLED";
    }
}

void ApplyThrust(Base6Directions.Direction direction, float percentage) {
    foreach (IMyThrust thruster in thrusters[direction]) thruster.ThrustOverridePercentage = percentage;
}

Boolean IsCollisionCourse(double stoppingDistance) {
    double THRESHOLD = 1000;
    forwardCamera.EnableRaycast = true;
    if (forwardCamera.CanScan(stoppingDistance+THRESHOLD))
        collidingEntity = forwardCamera.Raycast(stoppingDistance+THRESHOLD, 0, 0);
    if (collidingEntity.Equals(null) || collidingEntity.IsEmpty()) return false;
    autopilotStatus += $"COLLISION DETECTED: { collidingEntity.Type }";
    return true;
}

void OrientShip(MyCommandLine command) {
    MyWaypointInfo target = GetWaypointTarget(command);
    if (autopilotStatus.Contains("Invalid GPS target")) return;

    Runtime.UpdateFrequency |= UpdateFrequency.Update1;
    autopilotMission = $"{ command.Argument(1).ToUpper() } \"{ target.Name }\"";
    double pitch, yaw;
    GetDirectionTo(target.Coords, autopilotController, out pitch, out yaw);
    autopilotStatus += $"Yaw: { yaw.ToString("n2") }\nPitch: { pitch.ToString("n2") }\n";

    pitch = MathHelper.Clamp(pitch, -ATTITUDE_MAX, ATTITUDE_MAX);
    yaw = MathHelper.Clamp(yaw, -ATTITUDE_MAX, ATTITUDE_MAX);
    foreach (IMyGyro gyro in gyros)
        OverrideGyro(gyro, TransformVector(new Vector3D(pitch, yaw, 0), autopilotController, gyro),
                command.Argument(1).ToUpper().Equals("LOCK"));
}

void OverrideGyro(IMyGyro gyro, Vector3D rotationVector, Boolean isLock) {
    gyro.GyroOverride = true;
    if (Math.Abs(rotationVector.X) > ATTITUDE_TOLERANCE) gyro.Pitch = (float) (rotationVector.X * (ATTITUDE_RATE));
    else gyro.Pitch = 0;
    if (Math.Abs(rotationVector.Y) > ATTITUDE_TOLERANCE) gyro.Yaw = (float) (rotationVector.Y * (ATTITUDE_RATE));
    else gyro.Roll = 0;
    if (Math.Abs(rotationVector.Z) > ATTITUDE_TOLERANCE) gyro.Roll = (float) (rotationVector.Z * (ATTITUDE_RATE));
    else gyro.Yaw = 0;

    if (Math.Abs(rotationVector.Y) < ATTITUDE_TOLERANCE && Math.Abs(rotationVector.X) < ATTITUDE_TOLERANCE) {
        gyro.GyroOverride=false;
        if (!isLock) autopilotMission = "COMPLETE";
    }
}
    

Vector3D TransformVector(Vector3D rotation, IMyTerminalBlock origin, IMyTerminalBlock query) {
    Matrix originMatrix = origin.WorldMatrix;
    Matrix queryMatrix = query.WorldMatrix;
    Vector3D absoluteRotation = Vector3D.TransformNormal(rotation, originMatrix);
    Vector3D relativeRotation = Vector3D.TransformNormal(absoluteRotation, Matrix.Transpose(queryMatrix));
    return relativeRotation;
}

void GetDirectionTo(Vector3D TV, IMyTerminalBlock Origin, out double Pitch, out double Yaw) {
    Vector3D OV = Origin.GetPosition();
    Vector3D FV = OV + Origin.WorldMatrix.Forward;
    Vector3D DV = OV + Origin.WorldMatrix.Down;
    Vector3D RV = OV + Origin.WorldMatrix.Right;

    double TVOV = (OV - TV).Length();
    double TVFV = (FV - TV).Length();
    double TVDV = (DV - TV).Length();
    double TVRV = (RV - TV).Length();

    double ThetaP = Math.Acos((TVDV * TVDV - 1 - TVOV * TVOV) / (-2 * TVOV));
    double ThetaY = Math.Acos((TVRV * TVRV - 1 - TVOV * TVOV) / (-2 * TVOV));

    double RPitch = 90 - (ThetaP * 180 / Math.PI);
    double RYaw = 90 - (ThetaY * 180 / Math.PI);

    if (TVOV < TVFV) RPitch = 180 - RPitch;
    if (TVOV < TVFV) RYaw = 180 - RYaw;

    if (RPitch > 180) RPitch = -1 * (360 - RPitch);
    if (RYaw > 180) RYaw = -1 * (360 - RYaw);

    Pitch = RPitch;
    Yaw = RYaw;
}

void Display(List<IMyTextSurface> panels, string text, Boolean append = true) {
    foreach (IMyTextSurface panel in panels) panel.WriteText(text, append);
}
