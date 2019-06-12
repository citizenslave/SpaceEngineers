string SIC_GPS = "GPS:SIC:24379.54:165341.15:2651.77:";
MyWaypointInfo sicGps = new MyWaypointInfo();

const float ATTITUDE_RATE = 0.05f;
const float ATTITUDE_TOLERANCE = 0.05f;
const float ATTITUDE_MAX = 20f;

public Program() {
    Runtime.UpdateFrequency = UpdateFrequency.Update1;

    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType(blocks, AddPrefix);

    MyWaypointInfo.TryParse(SIC_GPS, out sicGps);
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
    return block.IsSameConstructAs(Me);
}

public void Save() {}

public void Main(string argument, UpdateType updateSource) {
    if ((updateSource & UpdateType.Update1) != 0) {
        updateSource &= ~UpdateType.Update1;
        IMyRemoteControl remote = GridTerminalSystem.GetBlockWithName("Shuttle - Remote Control") as IMyRemoteControl;

        IMyTextSurface display = (GridTerminalSystem.GetBlockWithName("Shuttle - Cockpit") as IMyCockpit).GetSurface(0);
        display.ContentType = ContentType.TEXT_AND_IMAGE;

        double pitch, yaw;
        GetDirectionTo(sicGps.Coords, remote, out pitch, out yaw);
        display.WriteText($"{pitch}, {yaw}\n\n");

        IMyGyro gyro = GridTerminalSystem.GetBlockWithName("Shuttle - Gyroscope") as IMyGyro;
        gyro.GyroOverride = true;
        if (Math.Abs(pitch) > ATTITUDE_MAX) pitch = ATTITUDE_MAX*(pitch<0?-1:1);
        if (Math.Abs(yaw) > ATTITUDE_MAX) yaw = ATTITUDE_MAX*(yaw<0?-1:1);

        if (Math.Abs(pitch) > ATTITUDE_TOLERANCE) gyro.Pitch = (float) (pitch * (ATTITUDE_RATE));
        else gyro.Pitch = 0;
        if (Math.Abs(yaw) > ATTITUDE_TOLERANCE) gyro.Roll = (float) (yaw * (ATTITUDE_RATE));
        else gyro.Roll = 0;

        if (Math.Abs(yaw) < ATTITUDE_TOLERANCE && Math.Abs(pitch) < ATTITUDE_TOLERANCE) gyro.GyroOverride=false;
    }
}

void GetDirectionTo(Vector3D TV, IMyTerminalBlock Origin, out double Pitch, out double Yaw) {
    Vector3D OV = Origin.GetPosition();
    Vector3D FV = OV + Origin.WorldMatrix.Forward;
    Vector3D UV = OV + Origin.WorldMatrix.Up;
    Vector3D RV = OV + Origin.WorldMatrix.Right;

    double TVOV = (OV - TV).Length();
    double TVFV = (FV - TV).Length();
    double TVUV = (UV - TV).Length();
    double TVRV = (RV - TV).Length();

    double ThetaP = Math.Acos((TVUV * TVUV - 1 - TVOV * TVOV) / (-2 * TVOV));
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
