function login_in(Data) {
    console.log("Hi");
    if (Data["Login"] == "Dima") {
        return true;
    } else {
        return false;
    }
}
function hi() {
    console.log("hi")
}

module.exports = { login_in, hi}; 