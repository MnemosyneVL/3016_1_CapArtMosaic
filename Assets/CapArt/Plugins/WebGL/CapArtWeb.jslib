mergeInto(LibraryManager.library, {

  CapArtDownload: function (namePtr, contentPtr, mimePtr) {
    var name = UTF8ToString(namePtr);
    var content = UTF8ToString(contentPtr);
    var mime = UTF8ToString(mimePtr);
    var blob = new Blob([content], { type: mime });
    var a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = name;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(function () { URL.revokeObjectURL(a.href); }, 5000);
  },

  CapArtPickFile: function (acceptPtr, objNamePtr, methodPtr) {
    var accept = UTF8ToString(acceptPtr);
    var objName = UTF8ToString(objNamePtr);
    var method = UTF8ToString(methodPtr);
    var input = document.createElement('input');
    input.type = 'file';
    input.accept = accept;
    input.style.display = 'none';
    document.body.appendChild(input);
    input.onchange = function () {
      if (!input.files || input.files.length === 0) {
        SendMessage(objName, method, '');
        document.body.removeChild(input);
        return;
      }
      var file = input.files[0];
      var reader = new FileReader();
      reader.onload = function (e) {
        var bytes = new Uint8Array(e.target.result);
        var bin = '';
        var chunk = 0x8000;
        for (var i = 0; i < bytes.length; i += chunk) {
          bin += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
        }
        SendMessage(objName, method, file.name + '|' + btoa(bin));
        document.body.removeChild(input);
      };
      reader.readAsArrayBuffer(file);
    };
    input.click();
  },

  CapArtSyncFS: function () {
    if (typeof FS !== 'undefined' && FS.syncfs) {
      FS.syncfs(false, function (err) { });
    }
  }

});
