/** Includes a file so its functionality may be included in the current file. Supported extension types include: .js, .css
 * @param {string} path Path to the included file. For example, "/scripts/includedScript.js" would include "includedScript.js" within the "scripts" folder in the same directory as the current script. **/
function include(path) {
    switch(path.split('.')[1]){
        case "js": document.write(`<script src="${path}"></script>`); break;
        case "css": document.write(`<link rel="stylesheet" href="${path}">`); break;
        default: throw new Error("Path (" + path + ") does not specify a file extension or the extension provided is non-valid. See the function description for a use of valid extensions.");
    }
}

include("stylesheet.css");