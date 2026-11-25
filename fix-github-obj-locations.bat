@echo off
REM Fix the GitHub upload: move the .obj models from the repo root into
REM their asset folders, and put the fixed .gitignore in place.
REM Run this from inside a clone of bayganik/mmGame-Engine-Stride-ECS:
REM copy this file into the clone's root folder and double-click it.
cd /d "%~dp0"
where git >nul 2>nul
if errorlevel 1 (
  echo Git is not on your PATH. Install Git for Windows, or run this from a VS2026 Developer Command Prompt.
  pause
  exit /b 1
)
git rev-parse --show-toplevel >nul 2>nul
if errorlevel 1 (
  echo This folder is not inside a git clone. Clone the repo first, then copy this file into the clone root and run it.
  pause
  exit /b 1
)
REM Move the models into their asset folders
git mv barracks.obj mmSamples/Assets/Obj/barracks.obj
git mv base.obj mmCityCenter/Assets/CityKit/base.obj
git mv bench.obj mmCityCenter/Assets/CityKit/bench.obj
git mv box_A.obj mmCityCenter/Assets/CityKit/box_A.obj
git mv box_B.obj mmCityCenter/Assets/CityKit/box_B.obj
git mv building_A.obj mmCityCenter/Assets/CityKit/building_A.obj
git mv building_A_withoutBase.obj mmCityCenter/Assets/CityKit/building_A_withoutBase.obj
git mv building_B.obj mmCityCenter/Assets/CityKit/building_B.obj
git mv building_B_withoutBase.obj mmCityCenter/Assets/CityKit/building_B_withoutBase.obj
git mv building_C.obj mmCityCenter/Assets/CityKit/building_C.obj
git mv building_C_withoutBase.obj mmCityCenter/Assets/CityKit/building_C_withoutBase.obj
git mv building_D.obj mmCityCenter/Assets/CityKit/building_D.obj
git mv building_D_withoutBase.obj mmCityCenter/Assets/CityKit/building_D_withoutBase.obj
git mv building_E.obj mmCityCenter/Assets/CityKit/building_E.obj
git mv building_E_withoutBase.obj mmCityCenter/Assets/CityKit/building_E_withoutBase.obj
git mv building_F.obj mmCityCenter/Assets/CityKit/building_F.obj
git mv building_F_withoutBase.obj mmCityCenter/Assets/CityKit/building_F_withoutBase.obj
git mv building_G.obj mmCityCenter/Assets/CityKit/building_G.obj
git mv building_G_withoutBase.obj mmCityCenter/Assets/CityKit/building_G_withoutBase.obj
git mv building_H.obj mmCityCenter/Assets/CityKit/building_H.obj
git mv building_H_withoutBase.obj mmCityCenter/Assets/CityKit/building_H_withoutBase.obj
git mv bush.obj mmCityCenter/Assets/CityKit/bush.obj
git mv cannon_barrel.obj mmCannon3D/Assets/cannon_barrel.obj
git mv cannon_carriage.obj mmCannon3D/Assets/cannon_carriage.obj
git mv car_hatchback.obj mmCityCenter/Assets/CityKit/car_hatchback.obj
git mv car_hatchback_full.obj mmCityCenter/Assets/CityKit/car_hatchback_full.obj
git mv car_hatchback_wheel_front_left.obj mmCityCenter/Assets/CityKit/car_hatchback_wheel_front_left.obj
git mv car_hatchback_wheel_front_right.obj mmCityCenter/Assets/CityKit/car_hatchback_wheel_front_right.obj
git mv car_hatchback_wheel_rear_left.obj mmCityCenter/Assets/CityKit/car_hatchback_wheel_rear_left.obj
git mv car_hatchback_wheel_rear_right.obj mmCityCenter/Assets/CityKit/car_hatchback_wheel_rear_right.obj
git mv car_police.obj mmCityCenter/Assets/CityKit/car_police.obj
git mv car_police_full.obj mmCityCenter/Assets/CityKit/car_police_full.obj
git mv car_police_wheel_front_left.obj mmCityCenter/Assets/CityKit/car_police_wheel_front_left.obj
git mv car_police_wheel_front_right.obj mmCityCenter/Assets/CityKit/car_police_wheel_front_right.obj
git mv car_police_wheel_rear_left.obj mmCityCenter/Assets/CityKit/car_police_wheel_rear_left.obj
git mv car_police_wheel_rear_right.obj mmCityCenter/Assets/CityKit/car_police_wheel_rear_right.obj
git mv car_sedan.obj mmCityCenter/Assets/CityKit/car_sedan.obj
git mv car_sedan_full.obj mmCityCenter/Assets/CityKit/car_sedan_full.obj
git mv car_sedan_wheel_front_left.obj mmCityCenter/Assets/CityKit/car_sedan_wheel_front_left.obj
git mv car_sedan_wheel_front_right.obj mmCityCenter/Assets/CityKit/car_sedan_wheel_front_right.obj
git mv car_sedan_wheel_rear_left.obj mmCityCenter/Assets/CityKit/car_sedan_wheel_rear_left.obj
git mv car_sedan_wheel_rear_right.obj mmCityCenter/Assets/CityKit/car_sedan_wheel_rear_right.obj
git mv car_stationwagon.obj mmCityCenter/Assets/CityKit/car_stationwagon.obj
git mv car_stationwagon_full.obj mmCityCenter/Assets/CityKit/car_stationwagon_full.obj
git mv car_stationwagon_wheel_front_left.obj mmCityCenter/Assets/CityKit/car_stationwagon_wheel_front_left.obj
git mv car_stationwagon_wheel_front_right.obj mmCityCenter/Assets/CityKit/car_stationwagon_wheel_front_right.obj
git mv car_stationwagon_wheel_rear_left.obj mmCityCenter/Assets/CityKit/car_stationwagon_wheel_rear_left.obj
git mv car_stationwagon_wheel_rear_right.obj mmCityCenter/Assets/CityKit/car_stationwagon_wheel_rear_right.obj
git mv car_taxi.obj mmCityCenter/Assets/CityKit/car_taxi.obj
git mv car_taxi_wheel_front_left.obj mmCityCenter/Assets/CityKit/car_taxi_wheel_front_left.obj
git mv car_taxi_wheel_front_right.obj mmCityCenter/Assets/CityKit/car_taxi_wheel_front_right.obj
git mv car_taxi_wheel_rear_left.obj mmCityCenter/Assets/CityKit/car_taxi_wheel_rear_left.obj
git mv car_taxi_wheel_rear_right.obj mmCityCenter/Assets/CityKit/car_taxi_wheel_rear_right.obj
git mv dumpster.obj mmCityCenter/Assets/CityKit/dumpster.obj
git mv firehydrant.obj mmCityCenter/Assets/CityKit/firehydrant.obj
git mv road_corner.obj mmCityCenter/Assets/CityKit/road_corner.obj
git mv road_corner_curved.obj mmCityCenter/Assets/CityKit/road_corner_curved.obj
git mv road_junction.obj mmCityCenter/Assets/CityKit/road_junction.obj
git mv road_straight.obj mmCityCenter/Assets/CityKit/road_straight.obj
git mv road_straight_crossing.obj mmCityCenter/Assets/CityKit/road_straight_crossing.obj
git mv road_tsplit.obj mmCityCenter/Assets/CityKit/road_tsplit.obj
git mv streetlight.obj mmCityCenter/Assets/CityKit/streetlight.obj
git mv trafficlight_A.obj mmCityCenter/Assets/CityKit/trafficlight_A.obj
git mv trafficlight_B.obj mmCityCenter/Assets/CityKit/trafficlight_B.obj
git mv trafficlight_C.obj mmCityCenter/Assets/CityKit/trafficlight_C.obj
git mv trash_A.obj mmCityCenter/Assets/CityKit/trash_A.obj
git mv trash_B.obj mmCityCenter/Assets/CityKit/trash_B.obj
git mv watertower.obj mmCityCenter/Assets/CityKit/watertower.obj
git mv barracks.mtl mmSamples/Assets/Obj/barracks.mtl
git mv barracks_diffuse.png mmSamples/Assets/Obj/barracks_diffuse.png
REM The file named "download" at the repo root IS the fixed .gitignore
REM (it was saved under the wrong name when downloaded). Put it in place.
move /Y download .gitignore
git add .gitignore
git commit -m "Move .obj models into their asset folders; stop ignoring *.obj"
git push
echo.
echo Done - check the repo on GitHub to confirm the models are in their folders.
pause
