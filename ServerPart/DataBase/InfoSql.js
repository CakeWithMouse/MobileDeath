const { request } = require('express');
var DataBase = require('./DataBase');
var fs = require('fs')

//User part
async function GetUserInfo(Data) {
    User = await DataBase.GetData('SELECT LastName,FirstName,Patronymic,Role FROM Users WHERE ID="' + Data['ID'] + '"');
    if (User.length != 0) {
        if (Data["UserRole"] === 1) {
            User[0]["Role"] = "Администратор";
        } else {
            User[0]["Role"] = "Проверяющий";
        }

        Competense = await DataBase.GetData('SELECT CompetenceID FROM CompetenceUser WHERE UserID="' + Data['ID'] + '"');
        User[0]["CompetenceID"] = Competense[0]["CompetenceID"];
        return User;
    } else {
        return { Error: "NotFound" };
    }
}
//Admin part
async function GetUsers() {
    Users = await DataBase.GetData('SELECT Users.ID as ID,LastName,FirstName,Patronymic,Role,Login,Password,Status,IDOrganization,StructuralDivision,Degree,Rank,Post,Phone,Email,Competence.ID as CompetenceID,Competence.Name as CompetenceName FROM Users JOIN CompetenceUser on Users.ID = CompetenceUser.UserID JOIN Competence on CompetenceUser.CompetenceID = Competence.ID');
    return Users;
}

async function ClearToken(Data) {
    result = await DataBase.GetData('DELETE FROM Session WHERE UserID="' + Data["ID"] + '"');
    return { "Status": "Success" }
}

module.exports = {GetUserInfo, GetUsers, ClearToken}

