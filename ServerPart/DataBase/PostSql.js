var DataBase = require('./DataBase');
async function RegistrateUser(Data) {
    User = await DataBase.GetData('Select * FROM Users WHERE Login ="' + Data["Login"] + '"');
    result = {}
    var today = new Date().toISOString().slice(0, 10);
    //console.log("Find User = ",User)
    if (User.length != 0) {
        result["Token"] = -1;
        return { "Status": "Error", "Error": "Already exsist Login" }
    } else {
        await DataBase.GetData('INSERT INTO Users SET LastName ="' + Data["LastName"]
            + '", FirstName="' + Data["FirstName"]
            + '", RegistrationDate="' + today
            + '", Patronymic="' + "none"//Data["IdUser"]
            + '", Role="' + 0
            + '", Login="' + Data["Login"]
            + '", Password ="' + Data["Password"]
            + '", Status="' + 1//Data["IdTeam"]
            + '", IDOrganization="' + 1//Data["IdTeam"]
            + '", StructuralDivision="' + "none"//Data["IdTeam"]
            + '", Degree="' + "none"//Data["IdTeam"]
            + '", Rank="' + "none"//Data["IdTeam"]
            + '", Post="' + "none"//Data["none"]
            + '", Phone="' + Data["Phone"]
            + '", Email="' + Data["Email"] + '"');
        User = await DataBase.GetData('Select * FROM Users WHERE Login ="' + Data["Login"] + '"');
        result["ID"] = User[0]["ID"];
        await DataBase.GetData('INSERT INTO CompetenceUser SET UserID ="' + result.ID + '", CompetenceID ="' + Data.Competence + '"')
    }
    return result;
}
async function UpdateUser(Data) {
    //console.log("Data to Update =",Data)
    await DataBase.GetData('UPDATE Users SET LastName ="' + Data["LastName"]
        + '", FirstName="' + Data["FirstName"]
        + '", Patronymic="' + Data["Patronymic"]
        + '", Login="' + Data["Login"]
        + '", Password ="' + Data["Password"]
        + '", Status="' + Data["Status"]
        //+ '", IDOrganization="' + Data["IDOrganization"]
        + '", IDOrganization=1'
        + ', StructuralDivision="' + Data["StructuralDivision"]
        + '", Degree="' + Data["Degree"]
        + '", Rank="' + Data["Rank"]
        + '", Post="' + Data["Post"]
        + '", Phone="' + Data["Phone"]
        + '", Email="' + Data["Email"]
        + '" WHERE ID ="' + Data["UserID"] + '"');
    await DataBase.GetData('UPDATE CompetenceUser SET CompetenceID ="' + Data.Competence
        + '" WHERE UserID ="' + Data["UserID"] + '"');

    return { "Status": "Success" };
}
module.exports = {
    RegistrateUser, UpdateUser}