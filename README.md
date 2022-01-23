# About

https://user-images.githubusercontent.com/84687350/147426602-d9b5d906-4318-4b63-9c06-0e688954f399.mp4

An attempt at converting C# to Logix

![The generated script is sent manually](https://raw.githubusercontent.com/vr-voyage/SharpLogix/main/screenshots/Variables-WriteBack-FirstWorkingExample.png)

A special branch of my NeosVR Plugin is required :
https://github.com/vr-voyage/voyage-neosvr-plugin/tree/SharpLogix

Also, a WebSocket server able to send the content
of the generated files is required, at the moment.  
I'm using my WebSocket Relay Server written with Godot
https://github.com/vr-voyage/websocket-relay-server

# Current status

First proof of concept kind-of-working.

Mostly a "Hello World" kinf of PoC. It's able to convert a
small script, with user function definition and calling.

If you display (unpack) the result, it's a mess, however,
it works and provide the expected results.

* Basic float arithmetic
* Basic if/else flows
* User defined functions, parameters and return

It's still far for being useable, though...

# Next steps

- [ ]
  Make a simple UI where the script to convert can be
  copy-pasted. Add the WebSocket server to it.
- [ ] 
  Handle basic flow control mechanisms
  (if/else and for loops)  
  **IN PROGRESS**
- [ ] 
  Autocasting for arithmetics. So that "1 + 1" use
  Add_Int and "1.0 + 1.0" use Add_Float.  Also, that
  "1.0 + 1" uses Add_Float with a IntToFloat cast in-between.
- [ ] 
  User-editable translation database for handling translation
  of existing C# method call to Logix call.
- [ ] 
  Proper Logix look on unpack. Some people suggested the
  use of "Blueprints", on "Remote Logix" twitter feed.

# Support

* Patreon : https://www.patreon.com/vrgames_voyage

You can also add me as "Voyage Voyage" on NeosVR, though
I mostly work offline generally.
