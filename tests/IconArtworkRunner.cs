#if ICON_ARTWORK_FIXTURE
using System;
class IconArtworkRunner{[STAThread]static int Main(){try{IconArtworkTests.Run((name,ok)=>{Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)throw new Exception(name);});return 0;}catch(Exception ex){Console.WriteLine(ex);return 1;}}}
#endif
