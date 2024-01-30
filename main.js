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

for (const slideshow of document.getElementsByClassName("e_slideshow")) {
    const content = document.createElement("div");
    content.setAttribute("class", "e_slideshow_content");
    while (slideshow.firstChild) {
        const slide_content = slideshow.firstChild;
        if (slide_content.tagName == null) { slideshow.removeChild(slide_content); continue; }

        slide_content.style.display = "none";
        slide_content.setAttribute("class", "e_slide");

        // slideshow.removeChild(slide_content);
        content.appendChild(slide_content);
    }

    const left_button = document.createElement("a");
    slideshow.appendChild(left_button);
    left_button.setAttribute("class", "e_slideshow_button");
    left_button.innerHTML = "&#10094";
    left_button.addEventListener("click", e => {
        let children = content.children;
        for (let i = 0; i < children.length; i++) {
            if (children[i].style.display == "block") {
                console.log(children[i].tagName);
                children[i].style.display = "none";
                children[(i + children.length - 1) % children.length].style.display = "block";
                break;
            }
        }
    });

    slideshow.appendChild(content);
    content.firstChild.style.display = "block";

    const right_button = document.createElement("a");
    slideshow.appendChild(right_button);
    right_button.setAttribute("class", "e_slideshow_button e_right");
    right_button.innerHTML = "&#10095";
    right_button.addEventListener("click", e => {
        let children = content.children;
        for (let i = 0; i < children.length; i++) {
            if (children[i].style.display == "block") {
                console.log(children[i].tagName);
                children[i].style.display = "none";
                children[(i + 1) % children.length].style.display = "block";
                break;
            }
        }
    });
}