(function(Global)
{
 "use strict";
 var WebSharper,Obj,Fs,Lib,Class1,Runtime;
 WebSharper=Global.WebSharper;
 Obj=WebSharper&&WebSharper.Obj;
 Fs=Global.Fs=Global.Fs||{};
 Lib=Fs.Lib=Fs.Lib||{};
 Class1=Lib.Class1=Lib.Class1||{};
 Runtime=WebSharper&&WebSharper.Runtime;
 Class1=Lib.Class1=Runtime.Class({
  get_X:function()
  {
   return"F#";
  }
 },Obj,Class1);
 Class1.New=Runtime.Ctor(function()
 {
  Obj.New.call(this);
 },Class1);
}(self));
