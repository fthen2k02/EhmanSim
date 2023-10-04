/*
* This code contains portions derived from https://github.com/MAFINS/MenyooSP/blob/b91068a18e6529f39bb881cd5303f20f562102e7/Solution/source/Scripting/GTAentity.cpp
*
* Copyright (C) 2015 crosire
*
* This software is  provided 'as-is', without any express  or implied  warranty. In no event will the
* authors be held liable for any damages arising from the use of this software.
* Permission  is granted  to anyone  to use  this software  for  any  purpose,  including  commercial
* applications, and to alter it and redistribute it freely, subject to the following restrictions:
*
*   1. The origin of this software must not be misrepresented; you must not claim that you  wrote the
*      original  software. If you use this  software  in a product, an  acknowledgment in the product
*      documentation would be appreciated but is not required.
*   2. Altered source versions must  be plainly  marked as such, and  must not be  misrepresented  as
*      being the original software.
*   3. This notice may not be removed or altered from any source distribution.
*/
/*
* ALTERED SOURCE
* Menyoo PC - Grand Theft Auto V single-player trainer mod
* Copyright (C) 2019  MAFINS
*/

/*
* This code also contains portions derived from https://github.com/MAFINS/MenyooSP/blob/b91068a18e6529f39bb881cd5303f20f562102e7/Solution/source/Menu/Routine.cpp
*
* Menyoo PC - Grand Theft Auto V single-player trainer mod
* Copyright (C) 2019  MAFINS
*
* This program is free software: you can redistribute it and/or modify
* it under the terms of the GNU General Public License as published by
* the Free Software Foundation, either version 3 of the License, or
* (at your option) any later version.
*/

/*
* ALTERED SOURCE
* "Ehman Simulator" PoC - Grand Theft Auto V PC mod
* Copyright (C) 2023 The Infinity Chiller
*/
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

namespace EhmanSim
{
    public class EhmanSim : Script
    {
        static class SharedConstants
        {
            public const int telescopesCount = 6;
            public const float maxZWorldCoords = 2699.999f;
            public const float demoObjVisibilityRange = 5f;
        }

        class RadioTelescope
        {
            const float blipsStartYZObjectCoords = 5.1357421875f; // center of the outermost dish circle, from cs6_04_satellite_dish.ydr

            Vector3 pos;
            int rotZ;
            /*
                On the minimap, long blips can move along with the camera, therefore we divide ours into smaller ones.
                blips[0] is still one-piece, because it is for the main map.
            */
            Blip[] blips;
            bool blipVisible;
            Color markerColor;

            public Vector3 position
            {
                get
                {
                    return pos;
                }
            }
            public int rotationZ
            {
                get
                {
                    return rotZ;
                }
            }

            public RadioTelescope(Vector3 position, int rotationZ, BlipColor blipColor, Color markerColor)
            {
                this.pos = position;
                this.rotZ = rotationZ;
                this.markerColor = markerColor;

                const float dishDiameter = 12.67041015625f * 2; // from cs6_04_satellite_dish.ydr
                const int blipDivisions = 50;

                blips = new Blip[blipDivisions + 1];

                float blipsEndYZObjectCoords = SharedConstants.maxZWorldCoords - position.Z;
                float blipMidYZObjectCoords = (blipsStartYZObjectCoords + blipsEndYZObjectCoords) / 2;

                Vector3 mid = position + directionVect(blipMidYZObjectCoords);
                float fullLength = blipsEndYZObjectCoords - blipsStartYZObjectCoords;
                blips[0] = new Blip(Function.Call<int>(Hash.ADD_BLIP_FOR_AREA, mid.X, mid.Y, 0f, dishDiameter, fullLength));

                float divLength = fullLength / blipDivisions;
                blipMidYZObjectCoords = blipsStartYZObjectCoords + divLength / 2;
                for (int i = 1; i <= blipDivisions; i++, blipMidYZObjectCoords += divLength)
                {
                    mid = position + directionVect(blipMidYZObjectCoords);
                    blips[i] = new Blip(Function.Call<int>(Hash.ADD_BLIP_FOR_AREA, mid.X, mid.Y, 0f, dishDiameter, divLength));
                }

                for (int i = 0; i <= blipDivisions; i++)
                {
                    blips[i].Rotation = -rotationZ;
                    blips[i].Color = blipColor;
                    blips[i].Alpha = 0;
                    blips[i].IsShortRange = true;
                    blips[i].DisplayType = i == 0 ? BlipDisplayType.MainMapSelectable : BlipDisplayType.MiniMapOnly;
                }
            }

            public Vector3 directionVect(float height)
            {
                double rad = rotZ * Math.PI / 180f;
                return new Vector3((float)Math.Sin(rad), (float)Math.Cos(rad), 1f) * height;
            }

            public void drawMarker()
            {
                World.DrawMarker(MarkerType.VerticalCylinder,
                    position + directionVect(blipsStartYZObjectCoords + 6.8f),
                    Vector3.Multiply(directionVect(1f), new Vector3(-1f, -1f, 1f)),
                    Vector3.Zero,
                    new Vector3(25.07f, 25.07f, 10000f),
                    markerColor);
            }

            public void toggleShowBlip()
            {
                blipVisible = !blipVisible;

                foreach (var b in blips)
                    b.Alpha = blipVisible ? 128 : 0;
            }

            public void deleteBlip()
            {
                if (blips != null)
                    foreach (var b in blips)
                        if (b != null)
                            b.Delete();
            }
        }

        class MovementManager
        {
            const float dishCenterYZObjectCoords = 1.93115234375f; // from cs6_04_satellite_dish.ydr

            static MovementManager instance = null;
            RadioTelescope[] telescopeArray;
            bool visibleBefore, collisionBefore, invincibleBefore;
            Vector3 posBefore;
            float headingBefore;
            Camera cam;
            bool lookForward = true;
            int currentTelescope;
            int startTime;
            float speed = 0.005f; // distance units (~meters) per millisecond
            float previousDist;

            public float speedInfo
            {
                get
                {
                    return speed;
                }
            }

            private MovementManager(RadioTelescope[] telescopeArray)
            {
                this.telescopeArray = telescopeArray;
            }

            public static MovementManager get(RadioTelescope[] telescopeArray)
            {
                if (instance == null)
                    instance = new MovementManager(telescopeArray);
                return instance;
            }

            /*
                Functions based on code from GTAentity.cpp in Menyoo
            */
            bool entityHasControl(Entity ent)
            {
                return Function.Call<bool>(Hash.NETWORK_HAS_CONTROL_OF_ENTITY, ent.Handle);
            }

            bool entityRequestControlOnce(Entity ent)
            {
                if (!Function.Call<bool>(Hash.NETWORK_IS_IN_SESSION) || entityHasControl(ent))
                    return true;

                Function.Call(Hash.SET_NETWORK_ID_CAN_MIGRATE, Function.Call<int>(Hash.NETWORK_GET_NETWORK_ID_FROM_ENTITY, ent), true);
                return Function.Call<bool>(Hash.NETWORK_REQUEST_CONTROL_OF_ENTITY, ent.Handle);
            }

            bool entityRequestControl(Entity ent)
            {
                int tick = 0;

                while (!entityRequestControlOnce(ent) && tick <= 12)
                    tick++;

                return tick <= 12;
            }

            /*
                Functions based on code from Routine.cpp in Menyoo
            */
            Entity myEntity()
            {
                Ped myPed = Game.Player.Character;

                if (myPed.IsInVehicle())
                    return myPed.CurrentVehicle;

                return myPed;
            }

            public bool setEnabled(bool enable)
            {
                Entity ent = myEntity();

                if (!ent.Exists())
                    return false;

                if (enable)
                {
                    visibleBefore = ent.IsVisible;
                    collisionBefore = ent.IsCollisionEnabled;
                    invincibleBefore = ent.IsInvincible;
                    posBefore = ent.Position;
                    headingBefore = ent.Heading;
                    currentTelescope = 0;
                    startTime = Game.GameTime;
                    previousDist = (float)(dishCenterYZObjectCoords * Math.Sqrt(2));
                }
                else
                {
                    entityRequestControl(ent);
                    ent.IsVisible = visibleBefore;
                    ent.IsCollisionEnabled = collisionBefore;
                    ent.IsInvincible = invincibleBefore;
                    ent.PositionNoOffset = posBefore;
                    ent.Heading = headingBefore;
                    ent.IsPositionFrozen = false;
                    Function.Call(Hash.ENABLE_CONTROL_ACTION, 2, GTA.Control.VehicleHorn, true);
                    Function.Call(Hash.ENABLE_CONTROL_ACTION, 2, GTA.Control.LookBehind, true);
                    Function.Call(Hash.ENABLE_CONTROL_ACTION, 2, GTA.Control.VehicleLookBehind, true);
                    Function.Call(Hash.ENABLE_CONTROL_ACTION, 2, GTA.Control.SelectWeapon, true);

                    if (cam != null && cam.Exists())
                    {
                        cam.IsActive = false;
                        cam.Delete();
                        World.RenderingCamera = null;
                        cam = null;
                    }
                }
                return true;
            }

            public bool onTick()
            {
                Entity ent = myEntity();

                if (!ent.Exists())
                    return true;

                Function.Call(Hash.DISABLE_CONTROL_ACTION, 2, GTA.Control.VehicleHorn, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 2, GTA.Control.LookBehind, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 2, GTA.Control.VehicleLookBehind, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 2, GTA.Control.SelectWeapon, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 2, GTA.Control.VehicleAccelerate, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 2, GTA.Control.VehicleBrake, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 2, GTA.Control.VehicleRadioWheel, true);

                if (cam == null || !cam.Exists())
                {
                    entityRequestControl(ent);
                    cam = World.CreateCamera(GameplayCamera.Position, GameplayCamera.Rotation, GameplayCamera.FieldOfView);
                    cam.AttachTo(ent, Vector3.Zero);
                    cam.DepthOfFieldStrength = 0f;
                    World.RenderingCamera = cam;
                }

                entityRequestControl(ent);
                ent.IsPositionFrozen = true;
                ent.IsCollisionEnabled = false;
                ent.IsVisible = false;
                ent.IsInvincible = true;
                Game.Player.Character.IsVisible = false;

                int currentTime = Game.GameTime;
                float currentDist = (currentTime - startTime) * speed;
                Vector3 myPos = telescopeArray[currentTelescope].position +
                    telescopeArray[currentTelescope].directionVect((float)((previousDist + currentDist) / Math.Sqrt(2)));
                if (myPos.Z > SharedConstants.maxZWorldCoords)
                {
                    currentTelescope++;
                    if (currentTelescope == SharedConstants.telescopesCount)
                    {
                        setEnabled(false);
                        return false;
                    }
                    startTime = currentTime;
                    previousDist = (float)(dishCenterYZObjectCoords * Math.Sqrt(2));
                    myPos = telescopeArray[currentTelescope].position +
                        telescopeArray[currentTelescope].directionVect(dishCenterYZObjectCoords);
                }
                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, ent.Handle, myPos.X, myPos.Y, myPos.Z, 0, 0, 0);

                float myRotZ = -telescopeArray[currentTelescope].rotationZ;
                if (!lookForward)
                    myRotZ += 180f;
                ent.Rotation = new Vector3(0f, 0f, myRotZ);
                cam.Rotation = new Vector3(45f * (lookForward ? 1 : -1), 0f, myRotZ);
                if (!Game.Player.IsAiming && !Game.Player.IsTargetingAnything)
                    Function.Call(Hash.SET_GAMEPLAY_CAM_RELATIVE_HEADING, 0f);

                return true;
            }

            public void toggleDirection()
            {
                lookForward = !lookForward;
            }

            public void amplifySpeed(float factor)
            {
                int currentTime = Game.GameTime;
                float currentDist = (currentTime - startTime) * speed;
                startTime = currentTime;
                previousDist += currentDist;
                speed *= factor;
            }

            public Vector3 getPosForNewProp()
            {
                const int timeUntilEnteringRange = 3000; // ms

                int currentTime = Game.GameTime;
                float dist = previousDist +
                    speed * (currentTime - startTime + timeUntilEnteringRange) +
                    SharedConstants.demoObjVisibilityRange;
                return telescopeArray[currentTelescope].position + telescopeArray[currentTelescope].directionVect((float)(dist / Math.Sqrt(2)));
            }
        }

        RadioTelescope[] telescopeArray;
        List<Prop> demoProps;
        bool movementEnabled, markersVisible, spotLight;
        GTA.UI.TextElement info;

        public EhmanSim()
        {
            this.Tick += onTick;
            this.KeyDown += onKeyDown;
            this.Aborted += onAbort;

            createTelescopes();
            demoProps = new List<Prop>();
            info = new GTA.UI.TextElement(null, new PointF(0, 0), 0.5f, Color.White, GTA.UI.Font.ChaletComprimeCologne, GTA.UI.Alignment.Left, false, true);
        }

        void onTick(object sender, EventArgs e)
        {
            if (movementEnabled && !MovementManager.get(telescopeArray).onTick())
                movementEnabled = false;

            if (movementEnabled && spotLight)
                World.DrawSpotLight(World.RenderingCamera.Position, World.RenderingCamera.Direction, Color.White, 1000f, 10f, 0f, 70f, 1f);

            if (markersVisible)
                foreach (var t in telescopeArray)
                    t.drawMarker();

            if (demoProps.Count > 0)
                updateDemoPropsVisibility();

            displayInfo();
        }

        void onKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.NumPad1:
                    foreach (var t in telescopeArray)
                        t.toggleShowBlip();
                    break;
                case Keys.NumPad3:
                    markersVisible = !markersVisible;
                    break;
                case Keys.NumPad5:
                    spotLight = !spotLight;
                    break;
                case Keys.NumPad7:
                    if (MovementManager.get(telescopeArray).setEnabled(!movementEnabled))
                        movementEnabled = !movementEnabled;
                    break;
                case Keys.NumPad9:
                    MovementManager.get(telescopeArray).toggleDirection();
                    break;

                case Keys.NumPad4:
                    if (movementEnabled)
                        MovementManager.get(telescopeArray).amplifySpeed(0.1f);
                    break;
                case Keys.NumPad6:
                    if (movementEnabled)
                        MovementManager.get(telescopeArray).amplifySpeed(10f);
                    break;

                case Keys.NumPad8:
                    spawnDemoProp();
                    break;
                case Keys.NumPad2:
                    deleteDemoProps();
                    demoProps.Clear();
                    break;
            }
        }

        void onAbort(object sender, EventArgs e)
        {
            if (movementEnabled)
                MovementManager.get(telescopeArray).setEnabled(false);

            foreach (var t in telescopeArray)
                t.deleteBlip();

            deleteDemoProps();
        }

        void createTelescopes()
        {
            telescopeArray = new RadioTelescope[SharedConstants.telescopesCount];

            // Positions and rotations result from cs6_04_strm_6.ymap.
            telescopeArray[0] = new RadioTelescope(new Vector3(1968.18957519531f, 2922.04858398438f, 58.8742904663086f),  15, BlipColor.Red4,        Color.FromArgb(80, Color.Red));
            telescopeArray[1] = new RadioTelescope(new Vector3(2003.81384277344f, 2934.90161132812f, 59.6766052246094f),  15, BlipColor.GolfPlayer3, Color.FromArgb(80, Color.Orange));
            telescopeArray[2] = new RadioTelescope(new Vector3(2045.48889160156f, 2949.28979492188f, 60.2232208251953f), -70, BlipColor.GolfPlayer2, Color.FromArgb(80, Color.Yellow));
            telescopeArray[3] = new RadioTelescope(new Vector3(2080.23242187500f, 2950.57495117188f, 59.1226806640625f),   0, BlipColor.Green,       Color.FromArgb(80, Color.Green));
            telescopeArray[4] = new RadioTelescope(new Vector3(2110.47509765625f, 2927.48120117188f, 60.1328735351562f),  25, BlipColor.Freemode,    Color.FromArgb(80, Color.Blue));
            telescopeArray[5] = new RadioTelescope(new Vector3(2141.32714843750f, 2904.03979492188f, 59.9694061279297f),  35, BlipColor.NetPlayer22, Color.FromArgb(80, Color.Magenta));
        }

        void spawnDemoProp()
        {
            if (!movementEnabled)
                return;

            Vector3 pos = MovementManager.get(telescopeArray).getPosForNewProp();
            if (pos.Z > SharedConstants.maxZWorldCoords)
                return;

            Prop demoProp = World.CreatePropNoOffset("p_cs_saucer_01_s", pos, false);
            demoProp.IsPositionFrozen = true;
            demoProps.Add(demoProp);
        }

        void updateDemoPropsVisibility()
        {
            Vector3 camPos = World.RenderingCamera.Position;
            
            if (camPos.Equals(Vector3.Zero))
                camPos = GameplayCamera.Position;

            foreach (Prop demoProp in demoProps)
            {
                bool visibility = camPos.DistanceTo(demoProp.Position) < SharedConstants.demoObjVisibilityRange;
                if (demoProp.IsVisible != visibility)
                    demoProp.IsVisible = visibility;
            }
        }

        void deleteDemoProps()
        {
            foreach (Prop demoProp in demoProps)
                demoProp.Delete();
        }

        void displayInfo()
        {
            string text = null;

            if (movementEnabled)
            {
                Vector3 pos = Game.Player.Character.Position;
                float speed = MovementManager.get(telescopeArray).speedInfo;
                text = string.Format("X: {0:F3}\nY: {1:F3}\nZ: {2:F3}\nSpeed: {3:F3} units/s\n", pos.X, pos.Y, pos.Z, speed * 1000);
            }

            text += string.Format("Demo objects: {0}", demoProps.Count);
            info.Caption = text;
            info.Draw();
        }
    }
}