# "Ehman Simulator" proof of concept

A simple mod for Grand Theft Auto V PC that moves the camera (together with the character) along the beam of each of the six radio telescopes in the Grand Senora Desert.

## Dependencies

* [ScriptHookV](http://www.dev-c.com/gtav/scripthookv/);
* [ScriptHookVDotNet](https://github.com/scripthookvdotnet/scripthookvdotnet/releases).

Tested with ScriptHookV v1.0.2944.0 and ScriptHookVDotNet v3.6.0.

## Installation

Just drop `EhmanSim.3.cs` into your `scripts` folder.

## Usage

* **NUM1** - enable/disable blips;
* **NUM3** - enable/disable cylindrical markers;
* **NUM5** - enable/disable the spotlight while following the beams;
* **NUM7** - start/stop movement along beams;
* **NUM9** - toggle between looking forward/backward;
* **NUM4/6** - decrease/increase speed;
* **NUM8** - spawn a demo object (Omega's small UFO) on the current beam, visible only within a 5-unit range, such that you will enter that range after 3 seconds if you keep the current speed;
* **NUM2** - delete all spawned demo objects.

## License

Most of the code is licensed under the GNU General Public License version 3 (GPLv3). Source content taken from other projects is tagged with the respective license in the source file.
