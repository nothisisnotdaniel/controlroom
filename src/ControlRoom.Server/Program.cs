using ControlRoom.Server;

return RoomKitApp.Run(args, ControlRoomApp.Options, ControlRoomApp.Map, new FactoryModule(ControlRoomApp.Jobs));
