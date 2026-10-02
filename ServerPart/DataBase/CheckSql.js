var DataBase = require('./DataBase');
async function login_in(Data) {
    Users = await DataBase.GetData('SELECT * FROM Users WHERE Login="' + Data['Login'] + '"');
    if (Users.length != 0) {
        if (Users[0]["Password"] == decodeURIComponent(Data["Password"])) {
            return Users[0];
        }
    }
    return [];
}
async function CheckTokenByToken(Token) {
    ID = await DataBase.GetData('SELECT UserID FROM Session WHERE Token="' + Token + '"')
    return ID;
}
async function CheckTokenByID(User) {
    Token = await DataBase.GetData('SELECT Token FROM Session WHERE UserID="' + User["ID"] + '"')
    return Token;
}
async function StartSession(Token, ID) {
    var today = new Date().toISOString().slice(0, 10);
    await DataBase.GetData('INSERT INTO Session SET UserID="' + ID + '",Token="' + Token +'",DataDelete="' + today +'"');
    return;
}
async function EndSession(Token) {

    await DataBase.GetData('DELETE FROM Session WHERE Token="' + Token + '"');
}
module.exports = { login_in, CheckTokenByToken, CheckTokenByID, StartSession, EndSession }