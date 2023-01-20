(function(Global)
{
 "use strict";
 var Cs,Web,Client,WebSharper,Obj,Template,Main,Vars,UI,Templating,Runtime,Server,TemplateInstance,Instance,MainForm,Vars$1,Instance$1,Cs$Web_Templates,Var$1,Remoting,AjaxRemotingProvider,Runtime$1,Handler,Doc,System,Guid,Client$1,Templates;
 Cs=Global.Cs=Global.Cs||{};
 Web=Cs.Web=Cs.Web||{};
 Client=Web.Client=Web.Client||{};
 WebSharper=Global.WebSharper;
 Obj=WebSharper&&WebSharper.Obj;
 Template=Web.Template=Web.Template||{};
 Main=Template.Main=Template.Main||{};
 Vars=Main.Vars=Main.Vars||{};
 UI=WebSharper&&WebSharper.UI;
 Templating=UI&&UI.Templating;
 Runtime=Templating&&Templating.Runtime;
 Server=Runtime&&Runtime.Server;
 TemplateInstance=Server&&Server.TemplateInstance;
 Instance=Main.Instance=Main.Instance||{};
 MainForm=Main.MainForm=Main.MainForm||{};
 Vars$1=MainForm.Vars=MainForm.Vars||{};
 Instance$1=MainForm.Instance=MainForm.Instance||{};
 Cs$Web_Templates=Global.Cs$Web_Templates=Global.Cs$Web_Templates||{};
 Var$1=UI&&UI.Var$1;
 Remoting=WebSharper&&WebSharper.Remoting;
 AjaxRemotingProvider=Remoting&&Remoting.AjaxRemotingProvider;
 Runtime$1=WebSharper&&WebSharper.Runtime;
 Handler=Server&&Server.Handler;
 Doc=UI&&UI.Doc;
 System=Global.System;
 Guid=System&&System.Guid;
 Client$1=UI&&UI.Client;
 Templates=Client$1&&Client$1.Templates;
 Client.ClientMain=function()
 {
  var vReversed;
  vReversed=Var$1.Create$1("");
  return(new MainForm.New()).Reversed(vReversed.get_View()).OnSend(function(e)
  {
   var $task,$run,$state,$await,rev;
   $task=new WebSharper.Task({
    status:3,
    continuations:[]
   });
   $state=0;
   $run=function()
   {
    var $this;
    $top:while(true)
     switch($state)
     {
      case 0:
       $await=void 0;
       $await=(new AjaxRemotingProvider.New()).Task("Cs.Web:Cs.Web.Remoting.DoSomething:-742509330",[($this=e.Vars,function()
       {
        return $this.instance.Hole("texttoreverse").$1;
       }($this)).Get()]);
       $state=1;
       $await.OnCompleted($run);
       return;
      case 1:
       if($await.exc)
        {
         $task.exc=$await.exc;
         $task.status=7;
         $task.RunContinuations();
         return;
        }
       rev=$await.result;
       vReversed.Set(rev);
       $task.status=5;
       $task.RunContinuations();
       return;
     }
   };
   $run();
  }).Doc();
 };
 Main=Template.Main=Runtime$1.Class({
  Doc:function()
  {
   return this.Create().get_Doc();
  },
  Create:function()
  {
   var completed,doc;
   completed=Handler.CompleteHoles(this.key,this.holes,[]);
   doc=Cs$Web_Templates.t(completed[0]);
   this.instance=new Instance.New(completed[1],doc);
   return this.instance;
  },
  scripts:function(x)
  {
   this.holes.push({
    $:2,
    $0:"scripts",
    $1:x
   });
   return this;
  },
  scripts$1:function(x)
  {
   this.holes.push({
    $:1,
    $0:"scripts",
    $1:x
   });
   return this;
  },
  scripts$2:function(x)
  {
   x=x??[];
   this.holes.push({
    $:0,
    $0:"scripts",
    $1:Doc.Concat(x)
   });
   return this;
  },
  scripts$3:function(x)
  {
   this.holes.push({
    $:0,
    $0:"scripts",
    $1:Doc.Concat(x)
   });
   return this;
  },
  scripts$4:function(x)
  {
   this.holes.push({
    $:0,
    $0:"scripts",
    $1:x
   });
   return this;
  },
  Body:function(x)
  {
   this.holes.push({
    $:2,
    $0:"body",
    $1:x
   });
   return this;
  },
  Body$1:function(x)
  {
   this.holes.push({
    $:1,
    $0:"body",
    $1:x
   });
   return this;
  },
  Body$2:function(x)
  {
   x=x??[];
   this.holes.push({
    $:0,
    $0:"body",
    $1:Doc.Concat(x)
   });
   return this;
  },
  Body$3:function(x)
  {
   this.holes.push({
    $:0,
    $0:"body",
    $1:Doc.Concat(x)
   });
   return this;
  },
  Body$4:function(x)
  {
   this.holes.push({
    $:0,
    $0:"body",
    $1:x
   });
   return this;
  },
  MenuBar:function(x)
  {
   this.holes.push({
    $:2,
    $0:"menubar",
    $1:x
   });
   return this;
  },
  MenuBar$1:function(x)
  {
   this.holes.push({
    $:1,
    $0:"menubar",
    $1:x
   });
   return this;
  },
  MenuBar$2:function(x)
  {
   x=x??[];
   this.holes.push({
    $:0,
    $0:"menubar",
    $1:Doc.Concat(x)
   });
   return this;
  },
  MenuBar$3:function(x)
  {
   this.holes.push({
    $:0,
    $0:"menubar",
    $1:Doc.Concat(x)
   });
   return this;
  },
  MenuBar$4:function(x)
  {
   this.holes.push({
    $:0,
    $0:"menubar",
    $1:x
   });
   return this;
  },
  Title:function(x)
  {
   this.holes.push({
    $:2,
    $0:"title",
    $1:x
   });
   return this;
  },
  Title$1:function(x)
  {
   this.holes.push({
    $:1,
    $0:"title",
    $1:x
   });
   return this;
  },
  $init:function()
  {
   this.key=Guid.NewGuid();
   this.holes=Runtime$1.MarkResizable([]);
   this.instance=null;
  }
 },Obj,Main);
 Main.New=Runtime$1.Ctor(function()
 {
  this.$init();
 },Main);
 Vars=Main.Vars=Runtime$1.Class({
  $init:function()
  {
   this.instance=null;
  }
 },null,Vars);
 Vars.New=Runtime$1.Ctor(function()
 {
  this.$init();
 },Vars);
 Vars.New$1=Runtime$1.Ctor(function(i)
 {
  this.$init();
  this.instance=i;
 },Vars);
 Instance=Main.Instance=Runtime$1.Class({
  get_Vars:function()
  {
   return new Vars.New$1(this);
  }
 },TemplateInstance,Instance);
 Instance.New=Runtime$1.Ctor(function(v,d)
 {
  TemplateInstance.New.call(this,v,d);
 },Instance);
 MainForm=Main.MainForm=Runtime$1.Class({
  Doc:function()
  {
   return this.Create().get_Doc();
  },
  Create:function()
  {
   var completed,doc;
   completed=Handler.CompleteHoles(this.key,this.holes,[["texttoreverse",0]]);
   doc=Cs$Web_Templates.mainform(completed[0]);
   this.instance=new Instance$1.New(completed[1],doc);
   return this.instance;
  },
  Reversed:function(x)
  {
   this.holes.push({
    $:2,
    $0:"reversed",
    $1:x
   });
   return this;
  },
  Reversed$1:function(x)
  {
   this.holes.push({
    $:1,
    $0:"reversed",
    $1:x
   });
   return this;
  },
  OnSend:function(x)
  {
   var $this;
   $this=this;
   function del(a,b)
   {
    return x({
     Vars:new Vars$1.New$1($this.instance),
     Target:a,
     Event:b
    });
   }
   this.holes.push({
    $:4,
    $0:"onsend",
    $1:function(a)
    {
     return function(b)
     {
      return del(a,b);
     };
    }
   });
   return this;
  },
  OnSend$1:function(x)
  {
   function del(a,b)
   {
    return x();
   }
   this.holes.push({
    $:4,
    $0:"onsend",
    $1:function(a)
    {
     return function(b)
     {
      return del(a,b);
     };
    }
   });
   return this;
  },
  OnSend$2:function(x)
  {
   var f;
   this.holes.push((f=x,{
    $:4,
    $0:"onsend",
    $1:function(el)
    {
     return function(ev)
     {
      return f(el,ev);
     };
    }
   }));
   return this;
  },
  TextToReverse:function(x)
  {
   this.holes.push({
    $:8,
    $0:"texttoreverse",
    $1:x
   });
   return this;
  },
  $init:function()
  {
   this.key=Guid.NewGuid();
   this.holes=Runtime$1.MarkResizable([]);
   this.instance=null;
  }
 },Obj,MainForm);
 MainForm.New=Runtime$1.Ctor(function()
 {
  this.$init();
 },MainForm);
 Vars$1=MainForm.Vars=Runtime$1.Class({
  $init:function()
  {
   this.instance=null;
  }
 },null,Vars$1);
 Vars$1.New=Runtime$1.Ctor(function()
 {
  this.$init();
 },Vars$1);
 Vars$1.New$1=Runtime$1.Ctor(function(i)
 {
  this.$init();
  this.instance=i;
 },Vars$1);
 Instance$1=MainForm.Instance=Runtime$1.Class({
  get_Vars:function()
  {
   return new Vars$1.New$1(this);
  }
 },TemplateInstance,Instance$1);
 Instance$1.New=Runtime$1.Ctor(function(v,d)
 {
  TemplateInstance.New.call(this,v,d);
 },Instance$1);
 Cs$Web_Templates.t=function(h)
 {
  Templates.LoadLocalTemplates("main");
  return h?Templates.NamedTemplate("main",null,h):void 0;
 };
 Cs$Web_Templates.mainform=function(h)
 {
  Templates.LoadLocalTemplates("main");
  return h?Templates.NamedTemplate("main",{
   $:1,
   $0:"mainform"
  },h):void 0;
 };
}(self));
